using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HardwareMonitorByYuinn.DataAccess.History;
using HardwareMonitorByYuinn.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HardwareMonitorByYuinn.Web.Controllers;

/// <summary>
/// Yerel ağa açma anahtarını yönetir: Ayarlar sayfası için durum/kaydet uç noktaları ve yerel ağdan
/// gelen ziyaretçiler için PIN giriş sayfası. Sahibinin kendi bilgisayarından (loopback) erişimi
/// bu denetimden hiçbir zaman geçmez — bkz. Program.cs'deki PIN kapısı ara katmanı.
/// </summary>
public sealed class RemoteAccessController : Controller
{
    private readonly RemoteAccessStartupSnapshot _startupSnapshot;
    private readonly IHistoryStore _historyStore;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Bir IP'nin art arda başarısız PIN denemesi sayısı, varsa kilit bitiş zamanı ve son deneme zamanı.</summary>
    private static readonly ConcurrentDictionary<IPAddress, (int Failures, DateTime LockedUntilUtc, DateTime LastAttemptUtc)> LoginAttempts = new();

    private const int MaxFailuresBeforeLockout = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    // Bu sözlük süreç ömrü boyunca (uygulama yeniden başlayana kadar) bellekte kalır ve normalde
    // yalnızca başarılı girişte (TryRemove) ya da burada temizlenir. Sürekli farklı IP'lerden
    // deneme yapan bir tarama/saldırı (ya da DHCP ile sık değişen LAN IP'leri) girdi sayısını
    // sınırsız büyütebilir. Girdi sayısı bu eşiği geçince, bir süredir hiç deneme yapılmamış
    // (bayat) girdiler süpürülür — ayrı bir arka plan zamanlayıcısına gerek kalmadan, yalnızca
    // Login çağrıldığında (zaten nadir bir yol) ucuz bir kontrolle.
    private const int PruneThreshold = 200;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private static void PruneStaleLoginAttemptsIfNeeded()
    {
        if (LoginAttempts.Count <= PruneThreshold) return;

        DateTime cutoff = DateTime.UtcNow - StaleAfter;
        foreach (KeyValuePair<IPAddress, (int Failures, DateTime LockedUntilUtc, DateTime LastAttemptUtc)> entry in LoginAttempts)
        {
            if (entry.Value.LastAttemptUtc < cutoff)
            {
                LoginAttempts.TryRemove(entry.Key, out _);
            }
        }
    }

    public RemoteAccessController(RemoteAccessStartupSnapshot startupSnapshot, IHistoryStore historyStore, IHostApplicationLifetime lifetime)
    {
        _startupSnapshot = startupSnapshot;
        _historyStore = historyStore;
        _lifetime = lifetime;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        ViewBag.Error = null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string pin, string? returnUrl = null)
    {
        RemoteAccessOptions current = RemoteAccessSettingsStore.Load();
        returnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        IPAddress? callerIp = HttpContext.Connection.RemoteIpAddress;

        PruneStaleLoginAttemptsIfNeeded();

        // LAN'daki bir cihazın PIN'i sınırsız deneyerek kaba kuvvetle bulmasını engellemek için art
        // arda başarısız denemelerden sonra kısa süreli kilitleme uygulanır.
        if (callerIp is not null && LoginAttempts.TryGetValue(callerIp, out var attempt) && DateTime.UtcNow < attempt.LockedUntilUtc)
        {
            _historyStore.RecordLoginAttempt(callerIp.ToString(), success: false, causedLockout: false, DateTime.UtcNow);
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Error = "Çok fazla hatalı deneme. Lütfen biraz sonra tekrar deneyin.";
            return View();
        }

        bool valid = !string.IsNullOrEmpty(current.PinHash) && !string.IsNullOrEmpty(current.PinSalt)
            && !string.IsNullOrWhiteSpace(pin)
            && FixedTimeEquals(RemoteAccessSettingsStore.HashPin(pin.Trim(), current.PinSalt), current.PinHash);

        if (!valid)
        {
            if (callerIp is not null)
            {
                int failures = (LoginAttempts.TryGetValue(callerIp, out var existing) ? existing.Failures : 0) + 1;
                bool locksOutNow = failures >= MaxFailuresBeforeLockout;
                DateTime lockedUntil = locksOutNow ? DateTime.UtcNow + LockoutDuration : DateTime.MinValue;
                LoginAttempts[callerIp] = (locksOutNow ? 0 : failures, lockedUntil, DateTime.UtcNow);
                _historyStore.RecordLoginAttempt(callerIp.ToString(), success: false, causedLockout: locksOutNow, DateTime.UtcNow);
            }

            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Error = "PIN hatalı.";
            return View();
        }

        if (callerIp is not null)
        {
            LoginAttempts.TryRemove(callerIp, out _);
            _historyStore.RecordLoginAttempt(callerIp.ToString(), success: true, causedLockout: false, DateTime.UtcNow);
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, "remote-user") };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (!Url.IsLocalUrl(returnUrl))
        {
            returnUrl = "/";
        }

        return LocalRedirect(returnUrl);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] bytesA = Encoding.UTF8.GetBytes(a);
        byte[] bytesB = Encoding.UTF8.GetBytes(b);
        return bytesA.Length == bytesB.Length && CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Status()
    {
        RemoteAccessOptions saved = RemoteAccessSettingsStore.Load();
        var lanAddresses = LanAddressHelper.GetIPv4Addresses();

        // "PIN gerekli mi" yalnızca ayar ETKİNKEN anlamlıdır (bkz. Program.cs'deki aynı formül:
        // pinRequired = enabled && PinHash var). Burada yalnızca ham PinHash varlığına bakılırsa,
        // remote access kapalıyken bile eskiden bir PIN kaydedilmiş olması "yeniden başlatma
        // gerekli" uyarısını SONSUZA DEK göstermeye devam ederdi — çünkü etkin olmayan bir PIN'in
        // varlığı, başlangıç anlık görüntüsündeki (etkinliğe bağlı) pinRequired ile hiç eşleşmezdi.
        bool savedPinRequired = saved.Enabled && !string.IsNullOrEmpty(saved.PinHash);

        // Aynı sebep: HTTPS de yalnızca ayar ETKİNKEN anlamlıdır (Program.cs'deki
        // httpsEnabled = remoteAccessEnabled && remoteAccess.HttpsEnabled formülüyle aynı). Bunu
        // atlayıp ham saved.HttpsEnabled'ı doğrudan karşılaştırmak, "Yerel ağdan erişime izin
        // ver"i kapatırken "HTTPS kullan" kutusu işaretli kalmışsa (bkz. Ayarlar sayfasındaki
        // otomatik kapatma), yeniden başlatma sonrasında bile "henüz etkin değil" uyarısının hiç
        // gitmemesine yol açıyordu — çünkü etkin olmayan bir HTTPS ayarının ham "true" değeri,
        // başlangıç anlık görüntüsündeki (etkinliğe bağlı, dolayısıyla false) değerle asla eşleşmezdi.
        bool savedEffectiveHttpsEnabled = saved.Enabled && saved.HttpsEnabled;

        return Json(new
        {
            activeEnabled = _startupSnapshot.Enabled,
            activePinRequired = _startupSnapshot.PinRequired,
            activeHttpsEnabled = _startupSnapshot.HttpsEnabled,
            savedEnabled = saved.Enabled,
            savedPinConfigured = !string.IsNullOrEmpty(saved.PinHash),
            savedHttpsEnabled = saved.HttpsEnabled,
            restartRequired = saved.Enabled != _startupSnapshot.Enabled
                || savedPinRequired != _startupSnapshot.PinRequired
                || savedEffectiveHttpsEnabled != _startupSnapshot.HttpsEnabled,
            lanAddresses,
            port = 5250,
            httpsPort = 5251
        });
    }

    private const int MinPinLength = 4;
    private const int MaxPinLength = 32;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Configure(bool enabled, string? pin, bool httpsEnabled = false)
    {
        RemoteAccessOptions current = RemoteAccessSettingsStore.Load();

        string? pinHash = current.PinHash;
        string? pinSalt = current.PinSalt;
        if (!string.IsNullOrWhiteSpace(pin))
        {
            string trimmedPin = pin.Trim();
            if (trimmedPin.Length < MinPinLength)
            {
                return BadRequest(new { error = $"PIN en az {MinPinLength} karakter olmalı." });
            }

            if (trimmedPin.Length > MaxPinLength)
            {
                return BadRequest(new { error = $"PIN en fazla {MaxPinLength} karakter olabilir." });
            }

            pinSalt = RemoteAccessSettingsStore.GenerateSalt();
            pinHash = RemoteAccessSettingsStore.HashPin(trimmedPin, pinSalt);
        }

        if (enabled && string.IsNullOrEmpty(pinHash))
        {
            return BadRequest(new { error = "Yerel ağa açmak için bir PIN belirlemelisiniz." });
        }

        RemoteAccessSettingsStore.Save(new RemoteAccessOptions { Enabled = enabled, PinHash = pinHash, PinSalt = pinSalt, HttpsEnabled = httpsEnabled });
        return Ok(new { success = true });
    }

    /// <summary>
    /// Kayıtlı kendinden imzalı sertifikayı siler; bir sonraki açılışta (SelfSignedCertificateProvider.
    /// GetOrCreate) güncel LAN IP'leriyle yenisi üretilir. Ev/ofis ağı değişip eski sertifikanın
    /// SAN listesi geçersiz kaldığında (ör. yönlendirici farklı bir IP atadığında) kullanılır.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RegenerateCertificate()
    {
        SelfSignedCertificateProvider.DeleteExisting();
        return Ok(new { success = true });
    }

    // Uygulama admin yetkisiyle çalıştığı için (app.manifest → requireAdministrator) kendini
    // elevated bir process olarak tekrar başlatmak, Windows'un doğası gereği YİNE bir UAC onayı
    // gerektirir (elevated bir process'ten sessizce elevated çocuk süreç açmanın standart bir yolu
    // yok). Bu yüzden burada "sessiz" bir yeniden başlatma vaat edilmiyor; kullanıcı arayüzünde de
    // bu açıkça belirtiliyor. Yeni process, ESKİ process'in portu (5250) gerçekten bıraktığından
    // emin olduktan SONRA başlatılır (ApplicationStopped'a kadar beklenir) — aksi hâlde yeni
    // process'in Kestrel'i "adres kullanımda" hatasıyla hiç açılmadan çökebilirdi.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Restart()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Yürütülebilir dosya yolu bulunamadı." });
        }

        _lifetime.ApplicationStopped.Register(() =>
        {
            try
            {
                // --no-open-browser: bu yeniden başlatma zaten kullanıcının o an açık olan
                // sekmesinden tetiklendi; yeni süreç bu yüzden tarayıcıda ekstra bir sekme açmaz
                // (bkz. Program.cs) — var olan sekme SignalR'ın otomatik yeniden bağlanmasıyla
                // kendiliğinden toparlanır.
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--no-open-browser",
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
                });
            }
            catch
            {
                // Kullanıcı UAC'ı reddetmiş olabilir; bu durumda uygulama kapalı kalır, kullanıcı
                // kısayoldan elle tekrar açabilir. Burada gösterecek bir arayüz artık yok (süreç kapanıyor).
            }
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            _lifetime.StopApplication();
        });

        return Ok(new { success = true });
    }

}
