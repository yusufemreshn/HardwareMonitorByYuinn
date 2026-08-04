using System.Runtime.InteropServices;
using HardwareMonitorByYuinn.DataAccess.Hardware.Native;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>Tek bir ekran kartının D3DKMT üzerinden okunan değerleri.</summary>
internal sealed class D3DKmtGpuReading
{
    public required string Luid { get; init; }
    public required string Name { get; init; }
    public double? TemperatureC { get; init; }
    public double? MemoryClockMhz { get; init; }
    public double? MemoryTotalMb { get; init; }
}

/// <summary>
/// Ekran kartı telemetrisini Windows'un çekirdek modu grafik arayüzünden (D3DKMT) okur.
/// Satıcıya özel bir kütüphaneye bağlı olmadığı için NVIDIA, AMD ve Intel kartlarında aynı şekilde çalışır;
/// ayrıca LibreHardwareMonitor'un okuyamadığı tümleşik ekran kartlarının sıcaklığını da verir.
/// </summary>
internal sealed class D3DKmtGpuProvider(ILogger logger)
{
    /// <summary>
    /// <c>KMTQAITYPE_ADAPTERPERFDATA</c> numarası Windows sürümleri arasında değişiyor: belgelerde 56 yazsa da
    /// bu makinede (Windows 11 26200) 62. Sabit yazmak yerine ilk okumada aday numaraları deneyip
    /// makul veri döndüreni seçeriz, böylece başka sürümlerde de çalışır.
    /// </summary>
    private static readonly uint[] PerfDataTypeCandidates = [62, 56, 61, 55, 63, 57, 60, 54, 64, 58, 59, 53];

    /// <summary>Gerçek bir ekran kartı belleği en az birkaç yüz MHz'de çalışır; altındaki değerler sürücünün "desteklemiyorum" cevabıdır.</summary>
    private const ulong MinimumPlausibleMemoryFrequencyHz = 1_000_000;

    /// <summary>Doğru sorgu türünü ararken kaç adaptörde deneyeceğimiz. Hiçbirinde bulunamazsa kaynak kalıcı olarak kapatılır.</summary>
    private const int MaxDiscoveryAttempts = 8;

    /// <summary>
    /// `PerfDataTypeCandidates` belgelenmemiş/tersine mühendislikle bulunmuş sayılar — hangi Windows
    /// sürümünde/sürücüde hangi numaranın gerçekte hangi çekirdek yapısına karşılık geldiği garanti
    /// değil. Yanlış (ama "başarılı" dönen) bir sorgu türü, sürücünün bizim `AdapterPerfData`
    /// struct'ımızdan DAHA BÜYÜK bir yapıyı aynı arabelleğe yazmaya çalışmasına yol açabilir — bu,
    /// projenin geçmişte yaşadığı açıklanamayan sessiz çökmelerin en olası nedeni olarak
    /// tanımlanmıştı (bkz. CHANGELOG.md). Arabelleği yalnızca struct boyutu kadar değil, bu payla
    /// ekstra ayırmak; sürücü beklenenden fazla yazsa bile komşu heap belleğine taşmayı önler.
    /// `PrivateDriverDataSize` yine de yalnızca gerçek struct boyutunu bildirir — davranış değişmez,
    /// sadece olası bir taşmaya karşı güvenlik payı eklenir.
    private const int NativeBufferSafetyPaddingBytes = 256;

    private readonly ILogger _logger = logger;
    private readonly object _gate = new();
    private uint? _perfDataType;
    private int _discoveryAttempts;
    private bool _enumerationFailureLogged;

    public IReadOnlyList<D3DKmtGpuReading> Read()
    {
        lock (_gate)
        {
            try
            {
                return ReadCore();
            }
            catch (Exception ex)
            {
                if (!_enumerationFailureLogged)
                {
                    _enumerationFailureLogged = true;
                    _logger.LogWarning(ex, "Ekran kartları D3DKMT üzerinden okunamadı, bu kaynak devre dışı bırakıldı");
                }

                return [];
            }
        }
    }

    private List<D3DKmtGpuReading> ReadCore()
    {
        var results = new List<D3DKmtGpuReading>();

        var query = new D3DKmt.EnumAdapters2 { NumAdapters = 0, PAdapters = nint.Zero };
        if (D3DKmt.D3DKMTEnumAdapters2(ref query) != D3DKmt.StatusSuccess || query.NumAdapters == 0)
            return results;

        int entrySize = Marshal.SizeOf<D3DKmt.AdapterInfo>();
        nint buffer = Marshal.AllocHGlobal(entrySize * (int)query.NumAdapters);
        try
        {
            query.PAdapters = buffer;
            if (D3DKmt.D3DKMTEnumAdapters2(ref query) != D3DKmt.StatusSuccess)
                return results;

            for (int i = 0; i < query.NumAdapters; i++)
            {
                var adapter = Marshal.PtrToStructure<D3DKmt.AdapterInfo>(buffer + i * entrySize);
                try
                {
                    D3DKmtGpuReading? reading = ReadAdapter(adapter);
                    if (reading is not null)
                        results.Add(reading);
                }
                finally
                {
                    var close = new D3DKmt.CloseAdapter { HAdapter = adapter.HAdapter };
                    D3DKmt.D3DKMTCloseAdapter(ref close);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private D3DKmtGpuReading? ReadAdapter(D3DKmt.AdapterInfo adapter)
    {
        string? name = QueryAdapterName(adapter.HAdapter);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        D3DKmt.AdapterPerfData? perf = QueryPerfData(adapter.HAdapter);
        double? memoryTotalMb = QueryDedicatedVideoMemoryMb(adapter.HAdapter);

        // Sıcaklık 1/10 °C cinsinden gelir. Sürücü desteklemiyorsa 0 döner; çalışan bir kart 0 °C
        // olamayacağı için bunu "veri yok" sayarız. Üst sınır, bozuk okumaları elemek içindir.
        double? temperature = null;
        double? memoryClock = null;
        if (perf is { } p)
        {
            double celsius = p.Temperature / 10d;
            if (celsius is > 0 and < 150)
                temperature = celsius;

            if (p.MemoryFrequency >= MinimumPlausibleMemoryFrequencyHz)
                memoryClock = p.MemoryFrequency / 1_000_000d;
        }

        return new D3DKmtGpuReading
        {
            Luid = adapter.AdapterLuid.ToString(),
            Name = name!.Trim(),
            TemperatureC = temperature,
            MemoryClockMhz = memoryClock,
            MemoryTotalMb = memoryTotalMb
        };
    }

    private static string? QueryAdapterName(uint adapterHandle)
    {
        int size = Marshal.SizeOf<D3DKmt.AdapterRegistryInfo>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            var info = new D3DKmt.QueryAdapterInfo
            {
                HAdapter = adapterHandle,
                Type = D3DKmt.QueryTypeAdapterRegistryInfo,
                PPrivateDriverData = buffer,
                PrivateDriverDataSize = (uint)size
            };

            if (D3DKmt.D3DKMTQueryAdapterInfo(ref info) != D3DKmt.StatusSuccess)
                return null;

            return Marshal.PtrToStructure<D3DKmt.AdapterRegistryInfo>(buffer).AdapterString;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static double? QueryDedicatedVideoMemoryMb(uint adapterHandle)
    {
        int size = Marshal.SizeOf<D3DKmt.SegmentSizeInfo>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            var info = new D3DKmt.QueryAdapterInfo
            {
                HAdapter = adapterHandle,
                Type = D3DKmt.QueryTypeSegmentSize,
                PPrivateDriverData = buffer,
                PrivateDriverDataSize = (uint)size
            };

            if (D3DKmt.D3DKMTQueryAdapterInfo(ref info) != D3DKmt.StatusSuccess)
                return null;

            var segments = Marshal.PtrToStructure<D3DKmt.SegmentSizeInfo>(buffer);

            // Tümleşik kartlarda ayrılmış video belleği 0'dır; o durumda sistem belleğinden paylaşılanı raporlarız.
            ulong bytes = segments.DedicatedVideoMemorySize > 0
                ? segments.DedicatedVideoMemorySize
                : segments.SharedSystemMemorySize;

            return bytes > 0 ? bytes / 1024d / 1024d : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private D3DKmt.AdapterPerfData? QueryPerfData(uint adapterHandle)
    {
        if (_perfDataType is { } known)
            return TryQueryPerfData(adapterHandle, known);

        // Tür bilinmiyorsa aramayı tek bir adaptörle sınırlamayız: ilk kart telemetri desteklemiyor
        // olabilir. Yine de sonsuza dek denemeyiz, aksi halde desteklemeyen sistemlerde her turda tarama yaparız.
        if (_discoveryAttempts >= MaxDiscoveryAttempts)
            return null;

        _discoveryAttempts++;
        foreach (uint candidate in PerfDataTypeCandidates)
        {
            D3DKmt.AdapterPerfData? result = TryQueryPerfData(adapterHandle, candidate);

            // Yalnızca çağrının başarılı dönmesi yetmez: aynı boyuttaki başka bir sorgu türü de başarılı
            // dönebilir. Sıcaklığın ya da bellek frekansının makul olması, doğru türü bulduğumuzu doğrular.
            if (result is not { } p || !IsPlausible(p))
                continue;

            _perfDataType = candidate;
            _logger.LogInformation("Ekran kartı performans verisi sorgu türü {Type} olarak belirlendi", candidate);
            return result;
        }

        if (_discoveryAttempts >= MaxDiscoveryAttempts)
        {
            _logger.LogInformation(
                "Ekran kartı performans verisi D3DKMT üzerinden okunamadı; sıcaklık ve bellek frekansı donanım kütüphanesinden alınacak");
        }

        return null;
    }

    private static bool IsPlausible(D3DKmt.AdapterPerfData data)
    {
        double celsius = data.Temperature / 10d;
        bool temperatureLooksReal = celsius is > 5 and < 150;
        bool memoryClockLooksReal = data.MemoryFrequency is >= MinimumPlausibleMemoryFrequencyHz and < 100_000_000_000;
        return temperatureLooksReal || memoryClockLooksReal;
    }

    private static D3DKmt.AdapterPerfData? TryQueryPerfData(uint adapterHandle, uint queryType)
    {
        int size = Marshal.SizeOf<D3DKmt.AdapterPerfData>();
        int allocatedSize = size + NativeBufferSafetyPaddingBytes;
        nint buffer = Marshal.AllocHGlobal(allocatedSize);
        try
        {
            // Fiziksel adaptör indeksi girdi alanıdır; bağlı olmayan (Linked Display Adapter) sistemlerde 0'dır.
            // Arabelleğin tamamı (güvenlik payı dahil) sıfırlanır; struct'ın ötesindeki bayt'lar zaten
            // hiç okunmaz, yalnızca taşma durumunda zarar görecek "tampon bölge" işlevi görür.
            for (int i = 0; i < allocatedSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            var info = new D3DKmt.QueryAdapterInfo
            {
                HAdapter = adapterHandle,
                Type = queryType,
                PPrivateDriverData = buffer,
                PrivateDriverDataSize = (uint)size
            };

            if (D3DKmt.D3DKMTQueryAdapterInfo(ref info) != D3DKmt.StatusSuccess)
                return null;

            return Marshal.PtrToStructure<D3DKmt.AdapterPerfData>(buffer);
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
