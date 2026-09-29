using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace BusinessOS.Pharmacy.Desktop.Diagnostics;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IDisposable
{
    private System.Windows.Application? _application;

    public void Attach(System.Windows.Application application)
    {
        if (_application is not null)
        {
            return;
        }

        _application = application;
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unhandled UI exception.");

        MessageBox.Show(
            "BusinessOS Pharmacy encountered an unexpected error. Your data has not been intentionally removed. Please retry the action; technical details were written to the application log.",
            "BusinessOS Pharmacy",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        logger.LogCritical(e.ExceptionObject as Exception, "Unhandled application-domain exception. IsTerminating={IsTerminating}", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unobserved background task exception.");
        e.SetObserved();
    }

    public void Dispose()
    {
        if (_application is null)
        {
            return;
        }

        _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _application = null;
    }
}
