using System.Drawing;

namespace HardwareMonitorByYuinn.Web.Desktop.Overlay;

/// <summary>Overlay'de gösterilebilecek tek bir metrik.</summary>
internal enum OverlayMetricKey
{
    CpuUsage,
    CpuTemp,
    GpuUsage,
    GpuTemp,
    Ram,
    Fps,
    CpuClock,
    CpuPower,
    GpuClock,
    GpuHotSpot,
    GpuPower,
    GpuVram,
    RamUsedGb,
    FpsLow1,
    FpsLow01,
    FrameTime,
    NetDown,
    NetUp,
}

internal enum OverlayCorner
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,

    /// <summary>
    /// Köşeye değil, <see cref="OverlaySettings.OffsetXPercent"/>/<see cref="OverlaySettings.OffsetYPercent"/>
    /// ile belirtilen serbest konuma yerleşir (Ayarlar sayfasındaki önizlemede sürükleyerek ayarlanır).
    /// </summary>
    Custom,
}

internal enum OverlayFontSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Overlay'deki metriklerin hangi sırada ve hangi satırlarda (yan yana mı alt alta mı) gösterileceğini
/// ayrıştırır/biçimlendirir. Serileştirilmiş biçim (kayıt defterinde ve Ayarlar sayfasıyla yapılan
/// HTTP alışverişinde kullanılan düz metin): satırlar <c>|</c> ile, bir satırdaki (yan yana
/// gösterilecek) metrikler <c>,</c> ile ayrılır — ör. <c>"CpuUsage,CpuTemp|GpuUsage|Ram,Fps"</c> üç
/// satır demektir: ilk satırda CPU kullanımı ve sıcaklığı yan yana, ikinci satırda tek başına GPU
/// kullanımı, üçüncü satırda RAM ve FPS yan yana.
/// </summary>
internal static class OverlayLayoutFormat
{
    internal const string DefaultLayout = "CpuUsage|CpuTemp|GpuUsage|GpuTemp|Ram|Fps";

    internal static List<List<OverlayMetricKey>> Parse(string raw)
    {
        List<List<OverlayMetricKey>> rows = [];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return rows;
        }

        foreach (string rowToken in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            List<OverlayMetricKey> row = [];
            foreach (string itemToken in rowToken.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse(itemToken.Trim(), out OverlayMetricKey key) && !row.Contains(key))
                {
                    row.Add(key);
                }
            }

            if (row.Count > 0)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>Boş dize (hiçbir metrik seçili değil) geçerli kabul edilir; yalnızca tanınmayan bir token varsa reddedilir.</summary>
    internal static bool IsValid(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        foreach (string rowToken in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string itemToken in rowToken.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Enum.TryParse<OverlayMetricKey>(itemToken.Trim(), out _))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Oyun içi izleme panelinin (overlay) tüm ayarları. Gerçek depo: <see cref="OverlaySettingsStore"/>.
/// Arka plan opaklığı (<see cref="BackgroundOpacity"/>) yalnızca arka plan dolgusunun alfa kanalını
/// etkiler — eski sürümdeki gibi tüm pencerenin (yazı dahil) opaklığı değil; bu yüzden yazılar
/// opaklık ne olursa olsun her zaman net/keskin kalır (bkz. OverlayForm.Render).
/// <see cref="Layout"/>, biçimi için <see cref="OverlayLayoutFormat"/>'a bakın; bilerek düz bir
/// dize olarak tutulur (iç içe liste değil) çünkü bu sınıf bir <c>record</c>, ve record'ların
/// otomatik eşitliği liste/dizi gibi referans tipli üyelerde İÇERİK değil REFERANS karşılaştırır —
/// her <see cref="OverlaySettingsStore.Load"/> çağrısı yeni liste nesneleri üretseydi, OverlayForm'un
/// "ayar gerçekten değişti mi" kontrolü (bkz. OverlayForm.Tick) her saniye yanlışlıkla "değişti"
/// derdi ve gereksiz yeniden çizime yol açardı; düz dize bu sorunu doğası gereği ortadan kaldırıyor.
/// </summary>
internal sealed record OverlaySettings(
    bool Enabled,
    string Layout,
    OverlayCorner Corner,
    double OffsetXPercent,
    double OffsetYPercent,
    OverlayFontSize FontSize,
    bool OnlyShowDuringGame,
    Color BackgroundColor,
    int BackgroundOpacity,
    Color LabelColor,
    Color ValueColor)
{
    internal static OverlaySettings Default { get; } = new(
        Enabled: false,
        Layout: OverlayLayoutFormat.DefaultLayout,
        Corner: OverlayCorner.TopRight,
        OffsetXPercent: 0,
        OffsetYPercent: 0,
        FontSize: OverlayFontSize.Medium,
        OnlyShowDuringGame: true,
        BackgroundColor: ColorTranslator.FromHtml("#10121A"),
        BackgroundOpacity: 80,
        LabelColor: ColorTranslator.FromHtml("#AAB4C3"),
        ValueColor: Color.White);
}
