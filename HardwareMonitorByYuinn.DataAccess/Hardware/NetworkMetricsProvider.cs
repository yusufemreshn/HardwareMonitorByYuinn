using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using HardwareMonitorByYuinn.Entity.Hardware;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>
/// Ağ indirme/yükleme hızını LibreHardwareMonitor'ın "Network" donanım sensörleri yerine .NET'in
/// kendi arayüz sayaçlarından hesaplar. Bu makinede LibreHardwareMonitor, her NDIS filtre katmanını
/// (Kaspersky, QoS Packet Scheduler, WFP vb.) ayrı bir "Network" donanımı olarak listeliyor — aynı
/// fiziksel trafiği birden fazla kez sayıp topluyor.
///
/// Tüm "Up" arayüzleri toplamak da güvenli değil: VPN/tünel istemcileri (ör. Cloudflare WARP)
/// kendi sanal arayüzlerini bazen <see cref="NetworkInterfaceType.Tunnel"/> dışında bir tip olarak
/// bildiriyor, bu da onları filtreden kaçırıp aynı trafiğin hem fiziksel adaptörde hem tünelde
/// sayılmasına (fiilen 2 katına çıkmasına) yol açıyor. Bunun yerine Windows'a "internete çıkışta şu
/// an gerçekten hangi arayüzü kullanıyorsun" diye soran <c>GetBestInterface</c> ile tek, doğru
/// arayüzü buluyoruz; iki ölçüm arasındaki bayt farkını süreye bölerek gerçek hızı elde ediyoruz.
/// </summary>
internal sealed class NetworkMetricsProvider
{
    private readonly object _gate = new();
    private (long Bytes, DateTime AtUtc)? _lastReceived;
    private (long Bytes, DateTime AtUtc)? _lastSent;
    private string? _lastInterfaceId;
    private double _totalDownloadedBytes;
    private double _totalUploadedBytes;

    // İnternete çıkan arayüzü bulmak (GetAllNetworkInterfaces + GetBestInterface P/Invoke) saniyede
    // bir tekrarlanan bu sağlayıcı için gereksiz pahalı bir iş — seçilen arayüz neredeyse hiç
    // değişmiyor. Seçim birkaç saniye önbelleğe alınır; asıl istatistik okuma (GetIPv4Statistics)
    // önbellek süresi içinde de her turda taze veri döner (arayüz nesnesinin kendisi değil, o an
    // sorgulanan sayaçlar önbelleğe alınıyor).
    private const double InterfaceRevalidationSeconds = 10;
    private NetworkInterface? _cachedInterface;
    private DateTime _interfaceCacheExpiresAtUtc = DateTime.MinValue;

    private NetworkInterface? FindInternetFacingInterfaceCached()
    {
        DateTime now = DateTime.UtcNow;
        if (_cachedInterface is not null && now < _interfaceCacheExpiresAtUtc)
            return _cachedInterface;

        _cachedInterface = FindInternetFacingInterface();
        _interfaceCacheExpiresAtUtc = now.AddSeconds(InterfaceRevalidationSeconds);
        return _cachedInterface;
    }

    [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
    private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    public NetworkSnapshot? Read()
    {
        lock (_gate)
        {
            NetworkInterface? nic = FindInternetFacingInterfaceCached();
            if (nic is null)
            {
                _lastReceived = null;
                _lastSent = null;
                _lastInterfaceId = null;
                return null;
            }

            // İnternete çıkan arayüz değiştiyse (ör. WARP açılıp kapandı, kablosuzdan kabloluya
            // geçildi) önceki sayaç artık farklı bir arayüze ait olur; sıfırdan başlarız. Oturum
            // toplamı ise arayüz değişse de birikmeye devam eder (kullanıcı için "bu oturumda ne
            // kadar veri kullandım" sorusu arayüzden bağımsızdır).
            if (nic.Id != _lastInterfaceId)
            {
                _lastReceived = null;
                _lastSent = null;
                _lastInterfaceId = nic.Id;
            }

            IPv4InterfaceStatistics stats;
            try
            {
                stats = nic.GetIPv4Statistics();
            }
            catch
            {
                return null;
            }

            DateTime now = DateTime.UtcNow;
            bool hasDownload = TryComputeRate(ref _lastReceived, stats.BytesReceived, now, out double downloadRate, ref _totalDownloadedBytes);
            bool hasUpload = TryComputeRate(ref _lastSent, stats.BytesSent, now, out double uploadRate, ref _totalUploadedBytes);

            if (!hasDownload && !hasUpload)
                return null;

            return new NetworkSnapshot
            {
                DownloadBytesPerSec = downloadRate,
                UploadBytesPerSec = uploadRate,
                AdapterName = nic.Name,
                LinkSpeedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000d : null,
                TotalDownloadedGb = _totalDownloadedBytes / 1024d / 1024d / 1024d,
                TotalUploadedGb = _totalUploadedBytes / 1024d / 1024d / 1024d
            };
        }
    }

    // Windows'un o an internete çıkışta gerçekten kullandığı tek arayüzü bulur. Bulunamazsa (ör.
    // internet bağlantısı yok) tüm "Up" fiziksel arayüzleri toplayan eski davranışa döner, ki bu
    // durumda zaten çift sayım riski yoktur (yalnızca bir tanesi gerçekten trafik taşıyacaktır).
    private static NetworkInterface? FindInternetFacingInterface()
    {
        uint destAddr = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0);
        NetworkInterface[] candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        try
        {
            if (GetBestInterface(destAddr, out uint bestIfIndex) == 0)
            {
                NetworkInterface? match = candidates.FirstOrDefault(n =>
                    n.GetIPProperties().GetIPv4Properties()?.Index == bestIfIndex);
                if (match is not null)
                    return match;
            }
        }
        catch
        {
            // GetBestInterface bu sistemde kullanılamıyorsa aşağıdaki geri dönüşe düşülür.
        }

        // IPv4 için en yüksek anlık toplam trafiğe sahip arayüz, en olası "gerçek" bağlantıdır.
        return candidates
            .Select(n =>
            {
                try { return (Nic: n, Total: n.GetIPv4Statistics().BytesReceived + n.GetIPv4Statistics().BytesSent); }
                catch { return (Nic: n, Total: 0L); }
            })
            .OrderByDescending(x => x.Total)
            .Select(x => x.Nic)
            .FirstOrDefault();
    }

    // İlk ölçümde bir önceki değer olmadığı için hız hesaplanamaz; yalnızca bu turun sayacı kaydedilir.
    private static bool TryComputeRate(ref (long Bytes, DateTime AtUtc)? last, long currentBytes, DateTime nowUtc, out double bytesPerSecond, ref double totalAccumulatorBytes)
    {
        bytesPerSecond = 0;
        bool hasRate = false;

        if (last is { } previous)
        {
            double seconds = (nowUtc - previous.AtUtc).TotalSeconds;
            if (seconds > 0 && currentBytes >= previous.Bytes)
            {
                bytesPerSecond = (currentBytes - previous.Bytes) / seconds;
                totalAccumulatorBytes += currentBytes - previous.Bytes;
                hasRate = true;
            }
        }

        last = (currentBytes, nowUtc);
        return hasRate;
    }
}
