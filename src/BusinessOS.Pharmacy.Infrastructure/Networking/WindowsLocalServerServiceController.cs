using System.Diagnostics;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.Infrastructure.Networking;

public sealed class WindowsLocalServerServiceController : ILocalServerServiceController
{
    public const string ServiceName = "BusinessOS Pharmacy Local Server";
    public const string FirewallRuleName = "Darmaltoon Local Server (Private LAN)";

    public async Task<LocalServerServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new LocalServerServiceStatus(
                false,
                false,
                false,
                "Windows Service status is available only on Windows.");
        }

        var result = await RunAsync(
            "sc.exe",
            $"query \"{ServiceName}\"",
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return new LocalServerServiceStatus(
                true,
                false,
                false,
                "The Darmaltoon Local Server Windows Service is not installed.");
        }

        var running = result.Output.Contains(
            "RUNNING",
            StringComparison.OrdinalIgnoreCase);

        return new LocalServerServiceStatus(
            true,
            true,
            running,
            running
                ? "Darmaltoon Local Server is running."
                : "Darmaltoon Local Server is installed but not running.");
    }

    public async Task<LocalServerServiceStatus> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await GetStatusAsync(cancellationToken);

        if (!current.IsWindows || !current.IsInstalled || current.IsRunning)
        {
            return current;
        }

        var result = await RunAsync(
            "sc.exe",
            $"start \"{ServiceName}\"",
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return current with
            {
                Message =
                    "Windows could not start the Darmaltoon Local Server. Run diagnostics or repair the server-service installation."
            };
        }

        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<FirewallConfigurationResult> EnsurePrivateFirewallRuleAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new FirewallConfigurationResult(
                false,
                "Windows Firewall configuration is available only on Windows.");
        }

        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var delete = await RunAsync(
            "netsh.exe",
            $"advfirewall firewall delete rule name=\"{FirewallRuleName}\"",
            cancellationToken);

        _ = delete;

        var add = await RunAsync(
            "netsh.exe",
            $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} profile=private",
            cancellationToken);

        return add.ExitCode == 0
            ? new FirewallConfigurationResult(
                true,
                $"Private-network firewall rule is configured for TCP {port}.")
            : new FirewallConfigurationResult(
                false,
                "Windows could not configure the private-network firewall rule. Administrator rights may be required.");
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            (await outputTask) + Environment.NewLine + (await errorTask));
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
