using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.LocalServer.Logging;

public sealed class NetworkFileLogger
{
    private readonly IApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NetworkFileLogger(IApplicationPaths paths) => _paths = paths;

    public async Task WriteAsync(
        string eventName,
        string message,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();

        var safeEvent = Sanitize(eventName, 120);
        var safeMessage = Sanitize(message, 800);
        var line = $"{DateTimeOffset.UtcNow:O}	{safeEvent}	{safeMessage}{Environment.NewLine}";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(
                _paths.NetworkLogPath,
                line,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Sanitize(string value, int maxLength)
    {
        value = value
            .Replace('', ' ')
            .Replace('
', ' ')
            .Replace('	', ' ');

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
