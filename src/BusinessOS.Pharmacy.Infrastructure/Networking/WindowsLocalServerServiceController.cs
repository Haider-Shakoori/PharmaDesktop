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

    public async Task<NetworkProfileStatus> GetNetworkProfileStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NetworkProfileStatus(
                false,
                false,
                false,
                false,
                "Windows network-profile detection is available only on Windows.");
        }

        var command =
            "Get-NetConnectionProfile | " +
            "Where-Object { $_.IPv4Connectivity -ne 'Disconnected' -or $_.IPv6Connectivity -ne 'Disconnected' } | " +
            "Select-Object -ExpandProperty NetworkCategory";

        var result = await RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -Command \"{command}\"",
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return new NetworkProfileStatus(
                true,
                false,
                false,
                false,
                "Windows network profile could not be determined.");
        }

        var categories = result.Output
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hasPrivateOrDomain = categories.Any(category =>
            category.Equals("Private", StringComparison.OrdinalIgnoreCase) ||
            category.Equals("DomainAuthenticated", StringComparison.OrdinalIgnoreCase));

        var hasPublic = categories.Any(category =>
            category.Equals("Public", StringComparison.OrdinalIgnoreCase));

        if (categories.Length == 0)
        {
            return new NetworkProfileStatus(
                true,
                false,
                false,
                false,
                "No connected Windows network profile was detected.");
        }

        return new NetworkProfileStatus(
            true,
            true,
            hasPrivateOrDomain,
            hasPublic,
            hasPrivateOrDomain
                ? "A Private/Domain Windows network is available for the pharmacy LAN."
                : hasPublic
                    ? "Windows currently identifies the connected network as Public. Do not expose the Darmaltoon Local Server until the pharmacy LAN is marked Private."
                    : "The connected Windows network profile is not suitable for the pharmacy LAN.");
    }

    public async Task<FirewallConfigurationResult> GetPrivateFirewallRuleStatusAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new FirewallConfigurationResult(
                false,
                "Windows Firewall status is available only on Windows.");
        }

        ValidatePort(port);

        var result = await RunAsync(
            "netsh.exe",
            $"advfirewall firewall show rule name=\"{FirewallRuleName}\" verbose",
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return new FirewallConfigurationResult(
                false,
                "The Darmaltoon private-LAN firewall rule is not installed.");
        }

        var mentionsPort = result.Output.Contains(
            port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
        var mentionsPrivate = result.Output.Contains(
            "Private",
            StringComparison.OrdinalIgnoreCase);

        return mentionsPort && mentionsPrivate
            ? new FirewallConfigurationResult(
                true,
                $"Private-network firewall rule is present for TCP {port}.")
            : new FirewallConfigurationResult(
                false,
                "A Darmaltoon firewall rule exists, but its port/profile does not match the current Server configuration.");
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

        ValidatePort(port);

        var profile = await GetNetworkProfileStatusAsync(cancellationToken);
        if (profile.HasPublicNetwork && !profile.HasPrivateOrDomainNetwork)
        {
            return new FirewallConfigurationResult(
                false,
                "Firewall rule was not opened because Windows identifies the connected network as Public. Change the trusted pharmacy LAN to Private, then try again.");
        }

        if (!profile.HasPrivateOrDomainNetwork)
        {
            return new FirewallConfigurationResult(
                false,
                "Firewall rule was not opened because no trusted Private/Domain network is currently available.");
        }

        _ = await RunAsync(
            "netsh.exe",
            $"advfirewall firewall delete rule name=\"{FirewallRuleName}\"",
            cancellationToken);

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

    private static void ValidatePort(int port)
    {
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
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
