using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

internal static partial class SensorLookup
{
    public static double? FindValue(this IHardware hardware, SensorType type, Func<string, bool> nameMatch)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType == type && sensor.Value.HasValue && nameMatch(sensor.Name))
                return sensor.Value.Value;
        }

        return null;
    }

    /// <summary>
    /// PawnIO sürücüsü kurulu değilken LibreHardwareMonitor, MSR/SMU tabanlı frekans, güç ve
    /// sıcaklık sensörleri için <c>null</c> yerine <c>0</c> döndürür. Çalışan bir donanım
    /// 0 MHz / 0 W / 0 °C olamayacağından bu okumaları "veri yok" olarak kabul ederiz.
    /// </summary>
    public static double? NullIfZero(this double? value) => value is > 0 ? value : null;

    [GeneratedRegex(@"Core #(\d+)")]
    public static partial Regex CoreIndexRegex();

    [GeneratedRegex(@"P-Core #(\d+)")]
    public static partial Regex PCoreIndexRegex();

    [GeneratedRegex(@"E-Core #(\d+)")]
    public static partial Regex ECoreIndexRegex();

    /// <summary>
    /// Intel P-core/E-core hibrit CPU'larda (ör. 12. nesil ve sonrası) LibreHardwareMonitor, Yük
    /// sensörlerini TÜM fiziksel çekirdekler için tek bir birleşik sırayla numaralandırır
    /// ("CPU Core #1".."#12": P-core'lar 1-4, E-core'lar 5-12) ama Frekans/Sıcaklık/Voltaj
    /// sensörlerinde bunun yerine <c>P-Core #1-4</c> ve <c>E-Core #1-8</c> gibi TÜR BAŞINA AYRI
    /// numaralandırma kullanır. "P-Core #N"/"E-Core #N" de düz <see cref="CoreIndexRegex"/> ile
    /// eşleştiği için (ikisi de "Core #N" alt dizesini içeriyor), E-core'ların 1'den başlayan
    /// numarası yanlışlıkla P-core'larla aynı indekslere (1-4) düşüyor ve üzerine yazıyordu; sonuçta
    /// E-core sayısı P-core sayısını aştığı için son birkaç fiziksel çekirdeğin (ör. 12 çekirdekli
    /// 4P+8E bir CPU'da 9-12) hiç Frekans/Voltaj sensörü kalmıyordu (ham sensör dökümüyle
    /// doğrulandı — bkz. CHANGELOG 2026-08-03). Bu yüzden E-Core'lar önce ayrıca yakalanıp
    /// <paramref name="pCoreCount"/> kadar kaydırılarak Yük sensörlerininkiyle aynı birleşik
    /// numaralandırmaya getiriliyor.
    /// </summary>
    public static int? ResolveCoreIndex(string sensorName, int pCoreCount)
    {
        Match eCoreMatch = ECoreIndexRegex().Match(sensorName);
        if (eCoreMatch.Success)
            return int.Parse(eCoreMatch.Groups[1].Value) + pCoreCount;

        Match pCoreMatch = PCoreIndexRegex().Match(sensorName);
        if (pCoreMatch.Success)
            return int.Parse(pCoreMatch.Groups[1].Value);

        Match generic = CoreIndexRegex().Match(sensorName);
        return generic.Success ? int.Parse(generic.Groups[1].Value) : null;
    }

    /// <summary>
    /// İş parçacığı başına ölçülen değerleri fiziksel çekirdek başına ortalamaya indirir.
    /// Aynı çekirdekteki iki iş parçacığı tek bir fiziksel çekirdeği paylaşır; frekans ve güç
    /// zaten çekirdek başına ölçüldüğü için yükün de aynı ölçekte olması gerekir, aksi hâlde
    /// tabloda çekirdek sayısından fazla satır oluşur ve fazlalıkların frekansı boş kalır.
    /// Sayılar tam katı değilse (beklenmeyen bir yerleşim) değerler olduğu gibi bırakılır.
    /// </summary>
    public static IReadOnlyDictionary<int, double> FoldThreadsIntoCores(
        IReadOnlyDictionary<int, double> perThread,
        int physicalCoreCount)
    {
        if (physicalCoreCount <= 0 || perThread.Count <= physicalCoreCount || perThread.Count % physicalCoreCount != 0)
            return perThread;

        int threadsPerCore = perThread.Count / physicalCoreCount;
        var folded = new Dictionary<int, double>(physicalCoreCount);
        for (int core = 1; core <= physicalCoreCount; core++)
        {
            double sum = 0;
            int counted = 0;

            // İş parçacıkları Windows'ta çekirdek çekirdek sıralanır: 1-2 → çekirdek 1, 3-4 → çekirdek 2 ...
            for (int offset = 0; offset < threadsPerCore; offset++)
            {
                if (perThread.TryGetValue((core - 1) * threadsPerCore + offset + 1, out double value))
                {
                    sum += value;
                    counted++;
                }
            }

            if (counted > 0)
                folded[core] = sum / counted;
        }

        return folded.Count > 0 ? folded : perThread;
    }
}
