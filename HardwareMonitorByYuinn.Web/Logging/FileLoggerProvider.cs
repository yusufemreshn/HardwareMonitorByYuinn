using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HardwareMonitorByYuinn.Web.Logging;

/// <summary>
/// .NET'in yerleşik loglama altyapısına dosyaya yazan bir hedef ekler. Seviye süzgeçleri, kategori
/// filtreleri ve bağımlılık enjeksiyonu altyapının kendisinden gelir; burada yalnızca yazma işi vardır.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogWriter _writer;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    public FileLoggerProvider(IOptions<FileLoggerOptions> options)
    {
        _writer = new FileLogWriter(options.Value);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer));

    public void Dispose()
    {
        _loggers.Clear();
        _writer.Dispose();
    }
}
