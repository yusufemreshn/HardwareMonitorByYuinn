using System.Drawing;
using HardwareMonitorByYuinn.Web.Desktop.Overlay;
using HardwareMonitorByYuinn.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HardwareMonitorByYuinn.Web.Controllers;

/// <summary>
/// Oyun içi izleme panelinin (overlay) Ayarlar sayfasından okunup yapılandırılması. Gerçek ayar
/// deposu Desktop/Overlay/OverlaySettingsStore.cs'dedir (HKCU); bu controller yalnızca okuma/yazma
/// uç noktalarıdır — <see cref="DesktopShellController"/> ile aynı şekil.
/// </summary>
public sealed class OverlayController : Controller
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public OverlayController(IStringLocalizer<SharedResource> localizer)
    {
        _localizer = localizer;
    }

    [HttpGet]
    public IActionResult Status()
    {
        OverlaySettings s = OverlaySettingsStore.Load();
        return Json(new
        {
            enabled = s.Enabled,
            layout = s.Layout,
            corner = s.Corner.ToString(),
            offsetX = s.OffsetXPercent,
            offsetY = s.OffsetYPercent,
            fontSize = s.FontSize.ToString(),
            onlyShowDuringGame = s.OnlyShowDuringGame,
            backgroundColor = ToHex(s.BackgroundColor),
            backgroundOpacity = s.BackgroundOpacity,
            labelColor = ToHex(s.LabelColor),
            valueColor = ToHex(s.ValueColor),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Configure(
        bool enabled,
        string layout,
        string corner,
        string offsetX,
        string offsetY,
        string fontSize,
        bool onlyShowDuringGame,
        string backgroundColor,
        int backgroundOpacity,
        string labelColor,
        string valueColor)
    {
        if (!Enum.TryParse(corner, out OverlayCorner parsedCorner))
        {
            return BadRequest(new { error = _localizer["Geçersiz değer."].Value });
        }

        if (!Enum.TryParse(fontSize, out OverlayFontSize parsedFontSize))
        {
            return BadRequest(new { error = _localizer["Geçersiz değer."].Value });
        }

        // double offsetX/offsetY olarak alınsaydı, ASP.NET Core model binder'ı bunu o anki istek
        // KÜLTÜRÜNE (uygulamanın varsayılanı Türkçe — ondalık ayıracı VİRGÜL) göre ayrıştırırdı;
        // ama tarayıcı tarafı (settings.js) JS Number.toString() ile NOKTA ondalık ayıracı kullanan
        // bir dize gönderiyor ("73.42" gibi). Türkçe kültürde "." grup (binlik) ayıracı sayıldığından
        // "73.42" "7342" gibi anlamsız büyük bir sayıya dönüşüp Math.Clamp ile 100'e yapışıyor,
        // overlay köşeye "sıçrıyordu" (gerçek kullanıcı geri bildirimi). Bu yüzden string olarak
        // alınıp InvariantCulture ile elle ayrıştırılıyor — tıpkı OverlaySettingsStore.ReadPercent'in
        // kayıt defterini okurken yaptığı gibi.
        if (!double.TryParse(offsetX, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedOffsetX)
            || !double.TryParse(offsetY, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedOffsetY))
        {
            return BadRequest(new { error = _localizer["Geçersiz değer."].Value });
        }

        // Kullanıcı tüm metrik kutularını kaldırırsa tarayıcı tarafı boş bir dize gönderir; bu,
        // "hiçbiri" anlamına gelir, geçersiz bir değer değil (bkz. OverlayLayoutFormat.IsValid).
        if (!OverlayLayoutFormat.IsValid(layout))
        {
            return BadRequest(new { error = _localizer["Geçersiz değer."].Value });
        }

        if (!TryParseColor(backgroundColor, out Color parsedBackgroundColor)
            || !TryParseColor(labelColor, out Color parsedLabelColor)
            || !TryParseColor(valueColor, out Color parsedValueColor))
        {
            return BadRequest(new { error = _localizer["Geçersiz değer."].Value });
        }

        var settings = new OverlaySettings(
            enabled,
            layout ?? string.Empty,
            parsedCorner,
            Math.Clamp(parsedOffsetX, 0, 100),
            Math.Clamp(parsedOffsetY, 0, 100),
            parsedFontSize,
            onlyShowDuringGame,
            parsedBackgroundColor,
            Math.Clamp(backgroundOpacity, 0, 100),
            parsedLabelColor,
            parsedValueColor);

        OverlaySettingsStore.Save(settings);
        return Ok(new { success = true });
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = ColorTranslator.FromHtml(hex);
            return true;
        }
        catch (Exception)
        {
            color = default;
            return false;
        }
    }
}
