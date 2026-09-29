using System.IO;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using Serilog;

namespace BusinessOS.Pharmacy.Desktop.Hosting;

public static class LoggingBootstrapper
{
    public static Serilog.ILogger CreateLogger(IApplicationPaths paths)
    {
        paths.EnsureCreated();

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "BusinessOS Pharmacy Desktop")
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "application-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }
}
