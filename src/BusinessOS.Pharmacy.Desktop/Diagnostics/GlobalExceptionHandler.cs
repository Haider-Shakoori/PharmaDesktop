using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace BusinessOS.Pharmacy.Desktop.Diagnostics;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IApplicationPaths paths) : IDisposable
{
    private System.Windows.Application? _application;
    private int _handlingUiException;

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

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unhandled UI exception.");

        // A Dispatcher exception can be raised repeatedly by the same broken
        // visual/timer. Never stack modal error windows on top of each other.
        if (Interlocked.Exchange(ref _handlingUiException, 1) != 0)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        var crashReportPath = WriteCrashReport(e.Exception);
        var detail = string.IsNullOrWhiteSpace(e.Exception.Message)
            ? e.Exception.GetType().Name
            : $"{e.Exception.GetType().Name}: {e.Exception.Message}";

        try
        {
            MessageBox.Show(
                $"Darmaltoon encountered an unexpected interface error.\n\n" +
                $"{detail}\n\n" +
                $"Your pharmacy data has not been intentionally removed. " +
                $"Technical details were written to:\n{crashReportPath}\n\n" +
                "Darmaltoon will close safely now. Reopen it after the issue is corrected.",
                "Darmaltoon",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _application?.Shutdown(-1);
        }
    }

    private string WriteCrashReport(Exception exception)
    {
        var path = Path.Combine(paths.LogsDirectory, "last-ui-error.txt");

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            File.WriteAllText(
                path,
                $"Darmaltoon UI crash\n" +
                $"UTC: {DateTimeOffset.UtcNow:O}\n" +
                $"Exception: {exception.GetType().FullName}\n" +
                $"Message: {exception.Message}\n\n" +
                exception);
        }
        catch (Exception writeException)
        {
            logger.LogError(
                writeException,
                "Could not write the UI crash report to {CrashReportPath}.",
                path);
        }

        return path;
    }

    private void OnAppDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        logger.LogCritical(
            e.ExceptionObject as Exception,
            "Unhandled application-domain exception. IsTerminating={IsTerminating}",
            e.IsTerminating);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
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
