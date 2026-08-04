using System.Runtime.InteropServices;

namespace HardwareMonitorByYuinn.DataAccess.Hardware.Native;

/// <summary>
/// Windows çekirdek modu grafik sürücüsü arayüzünün (DirectX Kernel Mode Thunk) kullanıcı modu girişleri.
/// Bu API her WDDM sürücüsünde bulunur; satıcıya özel bir SDK ya da kütüphane gerektirmez, dolayısıyla
/// NVIDIA, AMD ve Intel kartlarının hepsinde aynı kodla çalışır.
/// </summary>
internal static class D3DKmt
{
    private const string Gdi32 = "gdi32.dll";

    /// <summary>NTSTATUS başarı kodu.</summary>
    internal const int StatusSuccess = 0;

    /// <summary>Adaptörün kayıt defterindeki tanıtıcı adları. Enum değeri Windows sürümleri arasında sabittir.</summary>
    internal const uint QueryTypeAdapterRegistryInfo = 8;

    /// <summary>Adaptörün bellek segment boyutları. Enum değeri Windows sürümleri arasında sabittir.</summary>
    internal const uint QueryTypeSegmentSize = 3;

    [DllImport(Gdi32, ExactSpelling = true)]
    internal static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 adapters);

    [DllImport(Gdi32, ExactSpelling = true)]
    internal static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo info);

    [DllImport(Gdi32, ExactSpelling = true)]
    internal static extern int D3DKMTCloseAdapter(ref CloseAdapter adapter);

    /// <summary>
    /// Yerel <c>LUID</c> yapısı. Tek bir 64 bitlik alan olarak tanımlanamaz: C tarafında hizalama
    /// 4 bayttır ve 8 baytlık bir alan kullanmak kendisinden sonraki tüm alanları kaydırır.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public readonly override string ToString() => $"0x{(uint)HighPart:x8}_0x{LowPart:x8}";
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterInfo
    {
        public uint HAdapter;
        public Luid AdapterLuid;
        public uint NumOfSources;
        public int PresentMoveRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EnumAdapters2
    {
        public uint NumAdapters;
        public nint PAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryAdapterInfo
    {
        public uint HAdapter;
        public uint Type;
        public nint PPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CloseAdapter
    {
        public uint HAdapter;
    }

    /// <summary>Ekran kartının anlık performans telemetrisi. Sıcaklık 1/10 °C, güç 1/10 % TDP cinsindendir.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOc;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SegmentSizeInfo
    {
        public ulong DedicatedVideoMemorySize;
        public ulong DedicatedSystemMemorySize;
        public ulong SharedSystemMemorySize;
    }

    private const int MaxPath = 260;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AdapterRegistryInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string AdapterString;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string BiosString;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string DacType;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string ChipType;
    }
}
