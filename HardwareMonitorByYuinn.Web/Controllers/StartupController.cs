using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;

namespace HardwareMonitorByYuinn.Web.Controllers;

/// <summary>
/// Windows açılışında otomatik başlatmayı Görev Zamanlayıcı (Task Scheduler) üzerinden yönetir.
/// HKCU\...\Run anahtarı KASITLI OLARAK KULLANILMIYOR: uygulama app.manifest üzerinden her zaman
/// yönetici yetkisi istiyor (requireAdministrator) ve Windows, Run anahtarına yazılan admin-manifestolu
/// programları oturum açılışında OTOMATİK YÜKSELTEMEZ; bunun sonucu bir UAC istemi değil, sessiz bir
/// başlatma iptali: ne pencere açılır, ne hata, ne log. Bu, Microsoft'un belgelediği, atlanamayan bir
/// davranış. Task Scheduler'da "en yüksek yetkiyle çalıştır" (/RL HIGHEST) + "oturum açılışında"
/// (/SC ONLOGON) tetikleyicisi ise admin-manifestolu uygulamalar için desteklenen tek otomatik
/// başlatma yöntemi.
/// </summary>
public sealed class StartupController : Controller
{
    private const string TaskName = "HardwareMonitorByYuinn";
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // Program.cs bu bayrağı args'ta arayıp ShellForm'a "başlarken gizli aç" olarak iletir. Bu tercih
    // ayrı bir kayıt anahtarında DEĞİL, doğrudan görevin komut satırı argümanı olarak saklanır, böylece
    // manuel kısayolla açılan uygulama her zaman görünür başlar, yalnızca Windows'un kendisinin
    // tetiklediği açılış bu bayrağı taşır. Tek doğru kaynak yine görev tanımının kendisidir.
    private const string MinimizedArg = "--minimized";

    // Eski (çalışmayan) uygulama sürümlerinden kalmış olabilecek Run anahtarı girdisini temizlemek
    // için; bu anahtar artık yazılmıyor, sadece geçmiş sürümlerden kalıntı olarak siliniyor.
    private const string LegacyRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "HardwareMonitorByYuinn";

    [HttpGet]
    public IActionResult Status()
    {
        string exePath = GetExecutablePath();
        bool enabled = TryGetRegisteredCommand(out string? registeredPath, out bool startMinimized);

        return Json(new
        {
            enabled,
            // Uygulama farklı bir klasöre taşındıysa görevdeki yol artık geçerli değildir; arayüz bu
            // durumda kullanıcıya "yeniden kaydedin" diyebilsin diye ayrıca bildiriyoruz.
            pathMismatch = enabled && !string.IsNullOrEmpty(registeredPath) &&
                !string.Equals(registeredPath, exePath, StringComparison.OrdinalIgnoreCase),
            startMinimized,
            exePath
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Configure(bool enabled, bool startMinimized = false)
    {
        RemoveLegacyRunKeyEntryIfPresent();

        if (enabled)
        {
            string exePath = GetExecutablePath();
            var (exitCode, output) = RunSchtasks(psi =>
            {
                psi.ArgumentList.Add("/Create");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/TR");
                // Yol boşluk içerebileceğinden schtasks'ın kendi ayrıştırıcısı için kaçışlı iç
                // tırnak gerekiyor; .NET'in ArgumentList'i bu değeri tek bir argüman olarak
                // doğru şekilde tırnaklayıp geçirir. --minimized yalnızca kullanıcı işaretlediyse eklenir.
                psi.ArgumentList.Add(startMinimized ? $"\"{exePath}\" {MinimizedArg}" : $"\"{exePath}\"");
                psi.ArgumentList.Add("/SC");
                psi.ArgumentList.Add("ONLOGON");
                psi.ArgumentList.Add("/RL");
                psi.ArgumentList.Add("HIGHEST");
                psi.ArgumentList.Add("/F");
            });

            if (exitCode != 0)
            {
                return StatusCode(500, new { success = false, error = output });
            }
        }
        else
        {
            RunSchtasks(psi =>
            {
                psi.ArgumentList.Add("/Delete");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/F");
            });
            // Görev zaten yoksa schtasks hata döner; kullanıcı için bu bir sorun değil, yok sayılır.
        }

        return Ok(new { success = true });
    }

    private static bool TryGetRegisteredCommand(out string? registeredPath, out bool startMinimized)
    {
        registeredPath = null;
        startMinimized = false;

        var (exitCode, output) = RunSchtasks(psi =>
        {
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
            psi.ArgumentList.Add("/XML");
        });

        if (exitCode != 0)
        {
            return false;
        }

        // Metin çıktısı (/FO LIST) Windows arayüz diline göre yerelleştirildiğinden ("Task To Run"
        // yerine Türkçe sistemde başka bir başlık) ayrıştırılamaz; /XML çıktısı dilden bağımsızdır.
        try
        {
            var doc = XDocument.Parse(output);
            // /Create sırasında /TR'ye verdiğimiz değer zaten tırnaklı (boşluklu yollar için); schtasks
            // bu tırnakları XML'deki <Command> içinde de aynen saklıyor, o yüzden karşılaştırmadan önce
            // soyulması gerekiyor, yoksa tırnaksız exePath ile asla eşleşmez. --minimized argümanı ise
            // ayrı bir <Arguments> öğesinde tutuluyor.
            registeredPath = doc.Descendants(TaskNs + "Command").FirstOrDefault()?.Value.Trim('"');
            string? arguments = doc.Descendants(TaskNs + "Arguments").FirstOrDefault()?.Value;
            startMinimized = arguments?.Contains(MinimizedArg, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (System.Xml.XmlException)
        {
            // Ayrıştırılamazsa yalnızca "görev var" bilgisini döndür, yol karşılaştırması atlanır.
        }

        return true;
    }

    private static (int ExitCode, string Output) RunSchtasks(Action<ProcessStartInfo> configureArguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        configureArguments(psi);

        using Process process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, string.IsNullOrEmpty(stdout) ? stderr : stdout);
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

    private static void RemoveLegacyRunKeyEntryIfPresent()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: true);
            key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch
        {
            // En iyi çaba temizlik; başarısız olsa da yeni Task Scheduler tabanlı kayıt zaten iş görür.
        }
    }
}
