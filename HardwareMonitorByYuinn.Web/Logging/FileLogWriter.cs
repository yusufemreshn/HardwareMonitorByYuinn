using System.Collections.Concurrent;
using System.Text;

namespace HardwareMonitorByYuinn.Web.Logging;

/// <summary>
/// Log satırlarını arka planda tek bir iş parçacığından dosyaya yazar. Kayıt üreten iş parçacıkları
/// yalnızca kuyruğa ekleme yapar; disk yavaşlığı ya da dosya kilidi istekleri bekletmez.
/// </summary>
internal sealed class FileLogWriter : IDisposable
{
    /// <summary>Log dosyaları BOM'suz UTF-8 yazılır; bazı metin araçları dosya başındaki BOM'u satır içeriği sanıyor.</summary>
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly FileLoggerOptions _options;
    private readonly string _directory;
    private readonly BlockingCollection<string> _queue;
    private readonly Thread _worker;

    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private int _currentSequence;
    private bool _disposed;

    public FileLogWriter(FileLoggerOptions options)
    {
        _options = options;
        _directory = Path.IsPathRooted(options.Directory)
            ? options.Directory
            : Path.Combine(AppContext.BaseDirectory, options.Directory);

        _queue = new BlockingCollection<string>(Math.Max(1, options.MaxQueuedMessages));
        _worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "HardwareMonitorByYuinn log yazıcı"
        };
        _worker.Start();
    }

    public void Enqueue(string line)
    {
        if (_disposed)
            return;

        // Kuyruk doluysa kaydı sessizce atarız: TryAdd bloklamaz, böylece log üreten kod hiç beklemez.
        try
        {
            _queue.TryAdd(line);
        }
        catch (InvalidOperationException)
        {
            // Kuyruk kapatılmış; uygulama sonlanıyor demektir.
        }
    }

    private void ProcessQueue()
    {
        foreach (string line in _queue.GetConsumingEnumerable())
        {
            try
            {
                StreamWriter writer = ResolveWriter(line.Length);
                writer.WriteLine(line);

                // Kuyruk boşaldığında diske aktarırız: her satırda flush etmek gereksiz yere yavaşlatır,
                // hiç etmemek ise çökme anında son kayıtların kaybolmasına yol açar.
                if (_queue.Count == 0)
                    writer.Flush();
            }
            catch (Exception)
            {
                // Loglama hiçbir koşulda uygulamayı düşürmemeli. Dosya kilitli ya da disk doluysa
                // mevcut akışı bırakıp bir sonraki satırda yeniden açmayı deneriz.
                CloseWriter();
            }
        }

        CloseWriter();
    }

    private StreamWriter ResolveWriter(int incomingLength)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_writer is not null && today == _currentDate && _writer.BaseStream.Length + incomingLength <= _options.MaxFileSizeBytes)
            return _writer;

        if (_writer is not null && today == _currentDate)
            _currentSequence++;
        else
            _currentSequence = 0;

        CloseWriter();
        Directory.CreateDirectory(_directory);
        _currentDate = today;

        // Aynı gün içinde boyut sınırı aşılırsa dosya numarası artar; gün değişince sıfırlanır.
        // Uygulama yeniden başladığında var olan dosyayı kapatmak yerine sonuna ekleriz.
        while (true)
        {
            string path = BuildPath(_currentDate, _currentSequence);
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= _options.MaxFileSizeBytes)
            {
                _currentSequence++;
                continue;
            }

            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), FileEncoding);
            break;
        }

        CleanUpOldFiles();
        return _writer;
    }

    private string BuildPath(DateOnly date, int sequence)
    {
        string suffix = sequence == 0 ? string.Empty : $"_{sequence:000}";
        return Path.Combine(_directory, $"{_options.FileNamePrefix}-{date:yyyyMMdd}{suffix}.log");
    }

    /// <summary>Saklama süresini aşan günlere ait dosyaları siler, böylece log klasörü sınırsız büyümez.</summary>
    private void CleanUpOldFiles()
    {
        if (_options.RetainedFileCountLimit <= 0)
            return;

        try
        {
            DateTime cutoff = DateTime.Now.Date.AddDays(-_options.RetainedFileCountLimit);
            foreach (string path in Directory.EnumerateFiles(_directory, $"{_options.FileNamePrefix}-*.log"))
            {
                if (File.GetLastWriteTime(path) < cutoff)
                    File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Temizlik başarısız olursa loglamaya devam ederiz; bu kritik bir işlem değil.
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch (Exception)
        {
            // Akış zaten bozuksa kapatma da hata verebilir; yapacak bir şey yok.
        }

        _writer = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.CompleteAdding();

        // Bekleyen kayıtların yazılmasını bekleriz, ama kapanışı süresiz geciktirmeyiz.
        _worker.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();
    }
}
