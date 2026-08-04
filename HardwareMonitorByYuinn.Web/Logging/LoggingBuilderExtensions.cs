using Microsoft.Extensions.Logging.Configuration;

namespace HardwareMonitorByYuinn.Web.Logging;

public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Dosyaya yazan log hedefini kaydeder. Ayarlar <c>appsettings.json</c> içindeki "Logging:File"
    /// bölümünden okunur; seviye süzgeçleri diğer hedeflerle aynı şekilde "Logging:File:LogLevel" altında tanımlanır.
    /// </summary>
    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder)
    {
        builder.AddConfiguration();
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        builder.Services.AddOptions<FileLoggerOptions>()
            .BindConfiguration("Logging:File");

        return builder;
    }
}
