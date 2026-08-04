using System.Collections.Concurrent;
using System.Diagnostics;
using HardwareMonitorByYuinn.DataAccess.Hardware.Native;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>Tek bir kare hızı ölçümü ve hangi sürece ait olduğu.</summary>
internal sealed record FpsReading(
    int ProcessId,
    string ProcessName,
    double FramesPerSecond,
    double FrameTimeMs,
    double? Low1PercentFps,
    double? LowPoint1PercentFps);

/// <summary>
/// Oyunların gerçek kare hızını Windows'un kendi olay izleme altyapısından (ETW) ölçer.
/// LibreHardwareMonitor'un "Fullscreen FPS" sensörü bazı ekran kartlarında (özellikle NVIDIA'da)
/// hiç bulunmuyor ve bulunduğunda bile yalnızca ekrana doğrudan flip yapan (nadir) durumları
/// yakalıyor; bu yüzden bu ölçümü tamamen terk edip <c>Microsoft-Windows-DXGI</c> sağlayıcısının
/// "Present" olaylarını dinliyoruz — Intel'in PresentMon aracının da temel aldığı yöntem budur ve
/// ekran kartı üreticisinden tamamen bağımsızdır (DirectX 9/11/12 kullanan her uygulamada çalışır;
/// Vulkan/OpenGL uygulamaları bu olayları üretmediği için kapsam dışıdır).
/// </summary>
internal sealed class EtwFpsProvider : IDisposable
{
    private const string SessionName = "HardwareMonitorFps";
    private const string DxgiProviderName = "Microsoft-Windows-DXGI";

    // Microsoft-Windows-DXGI manifestindeki "Present" olayının başlangıcı. Bu sağlayıcı için
    // TraceEvent'te hazır bir ayrıştırıcı sınıf yok (yalnızca çekirdek sağlayıcılar için var),
    // bu yüzden olayı ham kimliğinden tanıyoruz.
    private const int PresentStartEventId = 42;

    /// <summary>Sunmayı kesen süreçlerin sayaçlarını bellekten temizleme sıklığı.</summary>
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(2);

    // Video oynatırken donanım hızlandırmalı render için DXGI present üreten, ama "oyun" olmayan
    // bilinen süreçler (medya oynatıcılar, tarayıcılar). Bu bir heuristiğin heuristiği — kesin bir
    // çözüm yok, DXGI kullanan her uygulama teknik olarak aynı yoldan geçiyor; bu yalnızca en sık
    // karşılaşılan yanlış-pozitifleri eler (ör. "HD-Player" bir medya oynatıcısının Oyun Geçmişi'nde
    // görünmesi), kapsamlı bir liste değildir.
    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "mpv", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "wmplayer", "potplayer",
        "potplayermini64", "smplayer", "hdplayer", "hd-player", "kmplayer", "gomplayer",
        "msedge", "chrome", "firefox", "brave", "opera", "iexplore",
        "SearchHost", "ApplicationFrameHost", "ShellExperienceHost"
    };

    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<int, ProcessPresentCounter> _countersByProcessId = new();
    private readonly TraceEventSession? _session;
    private readonly Thread? _processingThread;
    private int _lastLoggedForegroundPid = -1;
    // Son raporlanan (gerçekten kare sunan) sürecin PID'i — bkz. GetForegroundFps üzerindeki not.
    private int? _lastReportedProcessId;
    private DateTime _lastPruneUtc = DateTime.MinValue;
    private bool _disposed;

    public EtwFpsProvider(ILogger logger)
    {
        _logger = logger;

        try
        {
            _session = CreateSession();
            _session.EnableProvider(DxgiProviderName);
            _session.Source.Dynamic.All += OnEvent;

            _processingThread = new Thread(RunProcessingLoop)
            {
                IsBackground = true,
                Name = "HardwareMonitorByYuinn ETW kare hızı"
            };
            _processingThread.Start();
        }
        catch (Exception ex)
        {
            // Yönetici yetkisi yoksa ya da ETW alt sistemi bu makinede kullanılamıyorsa buraya düşer.
            // Kare hızı bu durumda sessizce "veri yok" döner; uygulamanın geri kalanını etkilemez.
            _logger.LogWarning(ex, "ETW kare hızı ölçümü başlatılamadı, bu özellik devre dışı kalacak");
            _session?.Dispose();
            _session = null;
        }
    }

    /// <summary>ETW alt sistemi başarıyla başlatılabildi mi. Başlatılamadıysa arayüz nedenini açıklayabilir.</summary>
    public bool IsAvailable => _session is not null;

    private static TraceEventSession CreateSession()
    {
        // Önceki bir çökme sonrası aynı isimde bir oturum kalmış olabilir; TraceEventSession bunu
        // kendisi devralıp yeniden yapılandırır, ayrıca bir temizlik adımına gerek yoktur.
        return new TraceEventSession(SessionName)
        {
            StopOnDispose = true
        };
    }

    private void RunProcessingLoop()
    {
        try
        {
            _session!.Source.Process();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ETW kare hızı dinleyicisi beklenmedik şekilde durdu");
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if ((int)data.ID != PresentStartEventId)
            return;

        int processId = data.ProcessID;
        if (processId <= 0)
            return;

        ProcessPresentCounter counter = _countersByProcessId.GetOrAdd(processId, static _ => new ProcessPresentCounter());
        counter.RecordPresent(data.TimeStamp.ToUniversalTime());
    }

    /// <summary>
    /// Ekrana o an aktif olarak kare sunan (kullanıcının önünde çalışan) uygulamanın kare hızı.
    /// Ölçülemiyorsa (uygulama DXGI kullanmıyor, henüz kare sunmadı ya da ETW kullanılamıyorsa) <c>null</c>.
    /// </summary>
    public FpsReading? GetForegroundFps()
    {
        if (_session is null)
            return null;

        DateTime nowUtc = DateTime.UtcNow;
        PruneStaleCounters(nowUtc);

        int? pid = User32.GetForegroundProcessId();
        LogForegroundChangeIfNeeded(pid);

        // Önce o anki gerçek ön plan sürecine bakılır — kendi kare sunma verisi varsa (asıl,
        // beklenen durum) her zaman o önceliklidir; böylece kullanıcı gerçekten başka (kare sunan)
        // bir uygulamaya geçtiğinde geçiş hemen yansır.
        if (pid is { } processId && !IsExcludedProcess(processId) &&
            _countersByProcessId.TryGetValue(processId, out ProcessPresentCounter? counter) &&
            counter.GetStats(nowUtc) is { } stats)
        {
            _lastReportedProcessId = processId;
            return new FpsReading(processId, ResolveProcessName(processId), stats.FramesPerSecond, stats.FrameTimeMs, stats.Low1PercentFps, stats.LowPoint1PercentFps);
        }

        // Ön plandaki pencerenin kendi present verisi YOKSA (Xbox Game Bar'ın kayıt göstergesi, bir
        // Windows bildirim balonu ya da bir overlay/Discord penceresi odağı bir anlığına çalmış
        // olabilir), bunu hemen gerçek bir uygulama değişikliği saymayız. En son başarıyla raporlanan
        // sürecin sayacı hâlâ tazeyse (son birkaç saniye içinde present üretmeye devam ediyorsa —
        // yani oyun aslında arka planda hâlâ render ediyorsa) onu raporlamaya devam ederiz. Bu,
        // kullanıcı tarafında oyunun "algılandı - kayboldu - tekrar algılandı" şeklinde çırpınmasını
        // (flicker) önler; oyun gerçekten durduğunda/küçültüldüğünde sayaç kendiliğinden bayatlar.
        if (_lastReportedProcessId is { } previousPid &&
            _countersByProcessId.TryGetValue(previousPid, out ProcessPresentCounter? previousCounter) &&
            previousCounter.GetStats(nowUtc) is { } previousStats)
        {
            return new FpsReading(previousPid, ResolveProcessName(previousPid), previousStats.FramesPerSecond, previousStats.FrameTimeMs, previousStats.Low1PercentFps, previousStats.LowPoint1PercentFps);
        }

        _lastReportedProcessId = null;
        return null;
    }

    // Ön plandaki süreç değiştiğinde bir kez loglarız; kare hızı beklenmedik göründüğünde ("bu
    // oyunun FPS'i olamayacak kadar düşük" gibi) ilk bakılacak yer burasıdır.
    private void LogForegroundChangeIfNeeded(int? pid)
    {
        int processId = pid ?? -1;
        if (processId == _lastLoggedForegroundPid)
            return;

        _lastLoggedForegroundPid = processId;
        if (pid is not { } validPid)
            return;

        bool hasCounter = _countersByProcessId.ContainsKey(validPid);
        _logger.LogInformation(
            "Ön plandaki süreç değişti: {ProcessName} (PID {ProcessId}), DXGI present verisi {HasData}",
            ResolveProcessName(validPid), validPid, hasCounter ? "var" : "henüz yok");
    }

    /// <summary>
    /// Uygulama uzun süre açık kalırken açılıp kapanan her sürecin sayacı kalıcı olarak bellekte
    /// kalmasın diye, sunmayı keseli (present üretmeyen) süreçlerin sayaçları periyodik olarak
    /// silinir. Süreç yeniden ön plana gelip present üretmeye başlarsa <see cref="OnEvent"/>
    /// zaten yeni bir sayaç oluşturur; veri kaybı olmaz.
    /// </summary>
    private void PruneStaleCounters(DateTime nowUtc)
    {
        if (nowUtc - _lastPruneUtc < PruneInterval)
            return;

        _lastPruneUtc = nowUtc;

        foreach (KeyValuePair<int, ProcessPresentCounter> entry in _countersByProcessId)
        {
            if (entry.Value.GetFps(nowUtc) is null)
                _countersByProcessId.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// Süreç adı kalıcı olarak önbelleğe alınmaz: Windows aynı PID'i (nadiren de olsa) başka bir
    /// sürece yeniden atayabilir; kalıcı önbellek bu durumda eski sürecin adını göstermeye devam
    /// ederdi. Bu yalnızca o an ön plandaki tek süreç için, saniyede bir çağrıldığından maliyeti önemsizdir.
    /// </summary>
    private static string ResolveProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"İşlem #{processId}";
        }
    }

    private static bool IsExcludedProcess(int processId) => ExcludedProcessNames.Contains(ResolveProcessName(processId));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _session?.Source.StopProcessing();
        }
        catch
        {
            // Oturum zaten kapanmışsa görmezden geliriz.
        }

        _session?.Dispose();
        _processingThread?.Join(TimeSpan.FromSeconds(2));
    }
}
