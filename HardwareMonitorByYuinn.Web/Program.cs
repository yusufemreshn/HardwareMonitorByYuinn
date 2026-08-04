using System.Net;
using HardwareMonitorByYuinn.Business;
using HardwareMonitorByYuinn.Web.Hubs;
using HardwareMonitorByYuinn.Web.Logging;
using HardwareMonitorByYuinn.Web.Security;
using HardwareMonitorByYuinn.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Hosting;

// Uygulama zaten çalışıyorken tekrar başlatılırsa (kısayola ikinci kez tıklama, oturum açılışı +
// elle başlatma çakışması vb.) burada hemen çıkılır; aksi hâlde her başlatma tarayıcıda yeni bir
// sekme açardı ve zaten çalışan örnek portu tuttuğu için Kestrel de patlardı. Mutex, işlem sonlanana
// kadar tutulmalı; bu yüzden en dıştaki kapsamda (top-level statements) yerel değişken olarak kalır.
using var singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Global\HardwareMonitorByYuinn_SingleInstance", out bool isFirstInstance);
if (!isFirstInstance)
{
    return;
}

// İçerik kökü açıkça uygulamanın bulunduğu klasöre sabitlenir. Varsayılan davranış onu çalışma
// dizininden alır; uygulama başka bir klasörden (kısayol, komut satırı, Görev Zamanlayıcı)
// başlatıldığında appsettings.json bulunamaz ve Kestrel yalnızca 127.0.0.1:5250 dinleme kuralı
// ile AllowedHosts ayarı sessizce devre dışı kalırdı.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Logging.AddFileLogger();

// "Yerel ağa açma" ayarı DI konteynerinden bağımsız, düz bir dosyadan (remote-access.json)
// okunur; çünkü Kestrel'in hangi adrese bağlanacağına builder.Build()'dan ÖNCE karar vermek
// gerekir. Ayar değişikliği yalnızca uygulama yeniden başlatıldığında etkili olur (Ayarlar
// sayfasında bu açıkça belirtilir).
RemoteAccessOptions remoteAccess = RemoteAccessSettingsStore.Load();
bool remoteAccessEnabled = remoteAccess.Enabled;
bool pinRequired = remoteAccessEnabled && !string.IsNullOrEmpty(remoteAccess.PinHash);
bool httpsEnabled = remoteAccessEnabled && remoteAccess.HttpsEnabled;

// Ayarlar sayfası "şu an etkin mi" bilgisini gösterebilsin diye başlangıçtaki anlık görüntü DI'ya
// da kaydedilir; diskteki güncel hâli (henüz yeniden başlatılmamış değişiklikler dahil)
// RemoteAccessController kendi içinde RemoteAccessSettingsStore.Load() ile ayrıca okur.
builder.Services.AddSingleton(new RemoteAccessStartupSnapshot(remoteAccessEnabled, pinRequired, httpsEnabled));

// "HTTPS kullan" açıksa LAN erişimi yalnızca şifreli 5251'den (kendinden imzalı sertifika, bkz.
// SelfSignedCertificateProvider) sunulur; sahibi kendi bilgisayarından yine düz HTTP ile 5250'den
// (yalnızca loopback, hiçbir zaman ağa açılmaz) erişmeye devam eder — özel bir IP adresi için
// gerçek bir sertifika otoritesinden (Let's Encrypt vb.) sertifika almak mümkün olmadığından bu,
// "hiç şifreleme yok" ile "gerçek bir CA" arasındaki tek pratik orta yol. HTTPS kapalıysa eskisi
// gibi tek adresten (127.0.0.1 ya da 0.0.0.0) düz HTTP.
builder.WebHost.ConfigureKestrel(options =>
{
    if (httpsEnabled)
    {
        options.ListenLocalhost(5250);
        System.Security.Cryptography.X509Certificates.X509Certificate2 cert = SelfSignedCertificateProvider.GetOrCreate();
        options.ListenAnyIP(5251, listenOptions => listenOptions.UseHttps(cert));
    }
    else
    {
        options.Listen(remoteAccessEnabled ? IPAddress.Any : IPAddress.Loopback, 5250);
    }
});

// ASP.NET Core'un host-filtering ara katmanı, appsettings.json'daki AllowedHosts değeriyle
// gelen isteklerin Host başlığını doğrular. Yerel ağa açıldığında istekler 127.0.0.1 dışındaki
// bir Host başlığıyla (ör. 192.168.1.5:5250) gelir; bu yüzden bu durumda sınırlama gevşetilir.
if (remoteAccessEnabled)
{
    builder.Configuration["AllowedHosts"] = "*";
}

// .NET'in varsayılan kapanma süresi 30 saniyedir — bir arka plan servisi (ör. donanım okuma
// döngüsü) iptal isteğini hemen fark etmezse (o an sürmekte olan senkron bir WMI/donanım
// sorgusunun ortasındaysa) host, "Yeniden Başlat"ın yeni süreci başlattığı ApplicationStopped'ı bu
// süre boyunca hiç tetiklemeyip bekletir. Bu uygulamada kapanırken tamamlanması ZORUNLU bir iş yok
// (en kötü ihtimalle o saniyenin bir tek örneği kaydedilmez); bu yüzden üst sınır kısa tutulup
// kullanıcının onlarca saniye beklemesi önlenir.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

const string LocalOnlyCorsPolicy = "LocalOnly";

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddBusinessServices();
builder.Services.AddHostedService<BroadcastBackgroundService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "hwmon_remote_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.LoginPath = "/RemoteAccess/Login";
        // API/hub istekleri için yönlendirme yerine düz 401/403 döndürülür.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs") ||
                context.Request.Headers.Accept.Any(a => a?.Contains("application/json") == true))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// WebSocket el sıkışması tarayıcı Same-Origin Policy'sine tabi değildir; SignalR hub'ını
// açıkça loopback (ve yerel ağa açıldıysa özel IP aralıklarındaki) kökenlerle sınırlamazsak,
// kullanıcının açtığı herhangi bir web sitesi ws://127.0.0.1 üzerinden canlı donanım verilerini
// sessizce dinleyebilir.
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalOnlyCorsPolicy, policy => policy
        .SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            // Yalnızca uygulamanın KENDİ portundan (5250) gelen loopback origin'lere güvenilir.
            // Eskiden herhangi bir loopback origin kabul ediliyordu — bu, aynı bilgisayarda çalışan
            // BAŞKA bir yerel web sunucusunun/uygulamanın (farklı bir portta) PIN olmadan
            // ws://127.0.0.1:5250/hubs/hardware'e bağlanıp canlı CPU/GPU/RAM/process-adı verisini
            // toplayabilmesine izin veriyordu.
            if (uri.IsLoopback && uri.Port == 5250)
            {
                return true;
            }

            return remoteAccessEnabled
                && IPAddress.TryParse(uri.Host, out IPAddress? ip)
                && IsPrivateLanAddress(ip);
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// "Yeniden Başlat" (RemoteAccessController.Restart), BU süreç henüz tam kapanmadan (ApplicationStopped
// sırasında) yeni bir örneği başlatır. Adlandırılmış Mutex nesnesi işletim sisteminde process
// tamamen kapanana/handle'ı kapanana kadar var olmaya devam eder — üstteki `using var` disposal'ı
// yalnızca en dışta app.Run() döndükten SONRA çalışır, yani yeni süreç başladığında eski süreç hâlâ
// tam olarak sonlanmamış olabilir ve yeni süreç mutex'i "zaten var" (createdNew=false) görüp hemen
// sessizce çıkardı — restart hiçbir hata/log bırakmadan başarısız olurdu. Mutex burada yalnızca bir
// "var/yok" işaretçisi olarak kullanıldığından (hiç WaitOne/ReleaseMutex ile kilitlenmiyor), Dispose
// çağrısı iş parçacığı sahipliği gerektirmez; kapanma sürecinin EN BAŞINDA (ApplicationStopping,
// Process.Start'ın çalıştığı ApplicationStopped'dan önce gelir) erkenden serbest bırakılır.
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { singleInstanceMutex.Dispose(); } catch { /* zaten kapanmışsa yapacak bir şey yok */ }
});

// Not: HardwareMonitorByYuinn.Web\Properties\launchSettings.json'daki "http" profili
// ASPNETCORE_ENVIRONMENT=Development ile geliyor (Visual Studio/`dotnet run` varsayılanı). Bu
// dosya (launchSettings.json) yalnızca kaynaktan `dotnet run`/IDE ile başlatıldığında etkilidir;
// derlenmiş .exe'yi doğrudan çalıştırdığınızda (bu uygulamanın normal kullanım şekli) hiç
// okunmaz, varsayılan olarak Production'da çalışır — bu yüzden IsDevelopment() aşağıda normal
// kullanımda hep false döner ve DeveloperExceptionPage hiç devreye girmez. Yine de ileride bir
// kurulum betiği/kısayolu bu profil üzerinden başlatılırsa stack trace/dosya yolu ifşası riski
// doğar; JSON yorum desteklemediği için bu uyarı doğrudan launchSettings.json'a değil buraya
// (ortamın gerçekten TÜKETİLDİĞİ yere) yazıldı.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Dashboard/Error");
}

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    // 'unsafe-inline' script/style için hâlâ gerekli — Razor görünümlerinde (ör. _Layout.cshtml'deki
    // tema/açılış-sekmesi betikleri) nonce'suz satır içi <script>/<style> kullanılıyor; bunu nonce
    // tabanlı daha sıkı bir politikaya çevirmek her görünümü tek tek değiştirmeyi gerektirir, bu
    // yüzden şimdilik kapsam dışı bırakıldı. Yine de gerçekçi asıl tehdit olan "üçüncü taraf bir
    // origin'den script/stil/görsel yükleme"yi (ör. XSS ile enjekte edilmiş <script src="https://
    // evil.example/x.js">) engeller. `img-src ... data:` Anlık Görüntü özelliğinin
    // canvas.toDataURL() ile ürettiği data: URI'ları için gerekli.
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Sahibinin kendi bilgisayarından (loopback) erişimi hiçbir zaman kesintiye uğramaz; PIN kapısı
// yalnızca yerel ağdan (loopback olmayan) gelen isteklere ve yalnızca bir PIN tanımlıysa uygulanır.
app.Use(async (context, next) =>
{
    if (!pinRequired)
    {
        await next();
        return;
    }

    bool isLoopbackCaller = context.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp);
    bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
    // Yalnızca giriş sayfası kimlik doğrulamasız erişilebilir olmalı. "Configure" ve "Status" gibi
    // diğer RemoteAccess uç noktaları BUNA dahil değildir; aksi hâlde PIN'i bilmeyen bir LAN
    // istemcisi Login sayfasından aldığı bir CSRF token'ıyla doğrudan Configure'a POST atıp PIN'i
    // kendi belirlediği bir değere değiştirebilir (kimlik doğrulamayı tamamen etkisiz kılar).
    bool isLoginPath = context.Request.Path.StartsWithSegments("/RemoteAccess/Login");
    bool isStaticAsset = context.Request.Path.StartsWithSegments("/css")
        || context.Request.Path.StartsWithSegments("/js")
        || context.Request.Path.StartsWithSegments("/lib")
        || context.Request.Path.StartsWithSegments("/favicon.ico");

    if (isLoopbackCaller || isAuthenticated || isLoginPath || isStaticAsset)
    {
        await next();
        return;
    }

    if (context.Request.Path.StartsWithSegments("/hubs"))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    string returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
    context.Response.Redirect($"/RemoteAccess/Login?returnUrl={returnUrl}");
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapHub<HardwareHub>("/hubs/hardware").RequireCors(LocalOnlyCorsPolicy);

// Geliştirme sırasında (`dotnet run`/IDE) her başlatmada tarayıcı açılması rahatsız edici olur;
// derlenmiş .exe'nin normal kullanımında (Production) Kestrel dinlemeye başladığı an tarayıcı açılır.
// Uygulama Ayarlar → Yerel Ağa Açma'daki "Yeniden Başlat" ile kendini kapatıp yeniden başlattığında
// bu adım atlanır (bkz. RemoteAccessController.Restart, --no-open-browser argümanını ekler):
// yeniden başlatma zaten kullanıcının o an açık olan sekmesinden tetiklendiği için bir sekme
// açık olduğu kesindir; SignalR bağlantısı (withAutomaticReconnect) sunucu geri gelince o sekmeyi
// kendiliğinden toparlar, ayrıca bir sekme açmaya gerek yoktur.
bool skipBrowserOpen = args.Contains("--no-open-browser");
if (!app.Environment.IsDevelopment() && !skipBrowserOpen)
{
    string startUrl = $"http://127.0.0.1:5250/";
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(startUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Kullanıcı elle tarayıcı açabilir; bu adım salt kolaylık, uygulamanın çalışmasını engellememeli.
        }
    });
}

app.Run();

static bool IsPrivateLanAddress(IPAddress address)
{
    IPAddress ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
    {
        return false;
    }

    byte[] bytes = ip.GetAddressBytes();
    return bytes[0] == 10
        || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        || (bytes[0] == 192 && bytes[1] == 168);
}
