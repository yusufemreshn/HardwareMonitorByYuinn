using System.Text;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.Web.Logging;

/// <summary>Tek bir kategori için log satırlarını biçimlendirip yazıcıya iletir.</summary>
internal sealed class FileLogger(string category, FileLogWriter writer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>Seviye süzgeci <c>appsettings.json</c> üzerinden loglama altyapısı tarafından uygulanır.</summary>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
            return;

        var builder = new StringBuilder(message.Length + 96);
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(ShortName(logLevel)).Append("] ")
            .Append(category);

        if (eventId.Id != 0)
            builder.Append('(').Append(eventId.Id).Append(')');

        builder.Append(": ").Append(message);

        if (exception is not null)
            builder.AppendLine().Append(exception);

        writer.Enqueue(builder.ToString());
    }

    // Seviye kısaltmaları log dosyalarında yerleşik olan İngilizce biçimde tutulur; böylece dosya
    // başka araçlarla incelendiğinde ya da internette aratıldığında tanıdık görünür.
    private static string ShortName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };
}
