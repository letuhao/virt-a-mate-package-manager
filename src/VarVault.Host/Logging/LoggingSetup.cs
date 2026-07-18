using Serilog;

namespace VarVault.Host.Logging;

/// <summary>Standard Serilog configuration: console + daily rolling file under the data directory.</summary>
public static class LoggingSetup
{
    public static Serilog.ILogger Create(string appName, string dataDirectory)
    {
        var logPath = System.IO.Path.Combine(dataDirectory, "logs", $"{appName}-.log");
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }
}
