using System.Windows;
using BusinessOS.Pharmacy.Desktop.Diagnostics;
using BusinessOS.Pharmacy.Desktop.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BusinessOS.Pharmacy.Desktop;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;

    public App()
    {
        _host = DesktopHost.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        _host.Services
            .GetRequiredService<GlobalExceptionHandler>()
            .Attach(this);

        await _host.Services
            .GetRequiredService<StartupCoordinator>()
            .StartAsync();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}