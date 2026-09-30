using System.Diagnostics;
using System.Net.Http.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanConnectionMonitor :
    BackgroundService,
    ILocalServerConnectionMonitor
{
    private static readonly TimeSpan HealthyInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    private readonly PinnedLocalServerTransport _transport;
    private readonly object _gate = new();
    private LocalServerConnectionStatus _current =
        new(false, null, null, null, null, "Pharmacy Server connection has not been checked yet.");

    public LanConnectionMonitor(PinnedLocalServerTransport transport)
    {
        _transport = transport;
    }

    public LocalServerConnectionStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<LocalServerConnectionStatus>? StatusChanged;

    public async Task<LocalServerConnectionStatus> CheckNowAsync(
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var client = await _transport.GetPairedClientAsync(cancellationToken);
            using var response = await client.GetAsync("health", cancellationToken);
            response.EnsureSuccessStatusCode();

            var health = await response.Content.ReadFromJsonAsync<LocalServerHealth>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("The pharmacy server returned an invalid health response.");

            var elapsed = Stopwatch.GetElapsedTime(started);
            var status = new LocalServerConnectionStatus(
                true,
                health.ServerId,
                null,
                elapsed,
                health.ServerTime,
                "Main Pharmacy Server connected.");

            Set(status);
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var previous = Current;
            var status = new LocalServerConnectionStatus(
                false,
                previous.ServerId,
                previous.ServerName,
                null,
                previous.LastSeenAt,
                $"Pharmacy Server unavailable. Reconnecting automatically. {exception.Message}");

            Set(status);
            return status;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failureIndex = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var status = await CheckNowAsync(stoppingToken);

            if (status.IsConnected)
            {
                failureIndex = 0;
                await Task.Delay(HealthyInterval, stoppingToken);
                continue;
            }

            var delay = RetryDelays[Math.Min(failureIndex, RetryDelays.Length - 1)];
            failureIndex++;
            await Task.Delay(delay, stoppingToken);
        }
    }

    private void Set(LocalServerConnectionStatus status)
    {
        bool changed;

        lock (_gate)
        {
            changed = _current != status;
            _current = status;
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, status);
        }
    }
}
