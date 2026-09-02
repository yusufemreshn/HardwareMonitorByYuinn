using System.Globalization;
using HardwareMonitorByYuinn.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class SettingsController : Controller
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public SettingsController(IStringLocalizer<SharedResource> localizer)
    {
        _localizer = localizer;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Language() => Json(new { language = LanguageSettings.GetLanguage() });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string language)
    {
        if (!LanguageSettings.SupportedLanguages.Contains(language))
        {
            return BadRequest(new { error = _localizer["Geçersiz değer."].Value });
        }

        LanguageSettings.SetLanguage(language);

        // Program.cs'deki başlangıç kültürüyle aynı mekanizma: RequestLocalization middleware
        // kullanılmadığından (bkz. Program.cs açıklaması), bu atama bir sonraki istekten itibaren
        // TÜM thread'lerde anında geçerli olur, yeniden başlatma gerekmez.
        CultureInfo culture = LanguageSettings.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        return Ok(new { success = true, language });
    }
}
