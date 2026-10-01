using System.Diagnostics;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Updater;

public static class UpdateApplier
{
    public static async Task<UpdateApplyResult> ApplyAsync(
        string planPath,
        CancellationToken cancellationToken = default)
    {
        var plan = JsonSerializer.Deserialize<UpdateApplyPlan>(
            await File.ReadAllTextAsync(planPath, cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Update apply plan is invalid.");

        ValidatePlan(plan);
        if (plan.WaitForProcessId > 0)
            await WaitForProcessExitAsync(plan.WaitForProcessId, cancellationToken);

        var serverWasStopped = false;
        if (OperatingSystem.IsWindows() &&
            string.Equals(plan.DeploymentMode, "Server", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(plan.LocalServerServiceName))
        {
            serverWasStopped = await StopWindowsServiceAsync(
                plan.LocalServerServiceName!,
                cancellationToken);
        }

        Directory.CreateDirectory(plan.RollbackDirectory);
        var newFiles = new List<string>();
        var replacedFiles = new List<(string Target, string Backup)>();        try
        {
            foreach (var source in Directory.EnumerateFiles(
                         plan.StagingDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(plan.StagingDirectory, source);
                ValidateRelativePath(relative);

                var target = Path.GetFullPath(
                    Path.Combine(plan.InstallationDirectory, relative));
                if (!UpdateService.IsSameOrChild(target, plan.InstallationDirectory))
                    throw new InvalidOperationException("Update file escaped the installation directory.");
                if (UpdateService.IsSameOrChild(target, plan.DataRootDirectory))
                    throw new InvalidOperationException("Update attempted to modify pharmacy data.");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    var backup = Path.Combine(plan.RollbackDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: true);
                    replacedFiles.Add((target, backup));
                }
                else
                {
                    newFiles.Add(target);
                }

                var temp = target + ".darmaltoon-new";
                File.Copy(source, temp, overwrite: true);                if (File.Exists(target))
                {
                    File.Replace(temp, target, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, target);
                }
            }

            await WriteSuccessMarkerAsync(plan, cancellationToken);

            if (serverWasStopped)
                _ = await StartWindowsServiceAsync(
                    plan.LocalServerServiceName!,
                    cancellationToken);

            RestartApplication(plan);
            return new(
                true,
                $"Darmaltoon {plan.TargetVersion} installed successfully.",
                plan.RollbackDirectory);
        }
        catch
        {
            foreach (var target in newFiles.AsEnumerable().Reverse())
            {
                if (File.Exists(target))
                    File.Delete(target);
            }

            foreach (var item in replacedFiles.AsEnumerable().Reverse())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                File.Copy(item.Backup, item.Target, overwrite: true);
            }            if (serverWasStopped)
            {
                try
                {
                    _ = await StartWindowsServiceAsync(
                        plan.LocalServerServiceName!,
                        CancellationToken.None);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    private static void ValidatePlan(UpdateApplyPlan plan)
    {
        if (plan.SchemaVersion != 1)
            throw new InvalidOperationException("Update apply plan schema is not supported.");
        if (!Version.TryParse(plan.TargetVersion, out _))
            throw new InvalidOperationException("Update target version is invalid.");

        var install = Path.GetFullPath(plan.InstallationDirectory);
        var data = Path.GetFullPath(plan.DataRootDirectory);
        var staging = Path.GetFullPath(plan.StagingDirectory);

        if (!Directory.Exists(staging))
            throw new InvalidOperationException("Update staging directory does not exist.");
        if (UpdateService.IsSameOrChild(data, install) ||
            UpdateService.IsSameOrChild(install, data))
            throw new InvalidOperationException(
                "Installation and pharmacy data directories are not safely isolated.");

        if (!string.IsNullOrWhiteSpace(plan.PreUpdateBackupPath) &&
            !File.Exists(plan.PreUpdateBackupPath))
            throw new InvalidOperationException(
                "The required pre-update pharmacy backup does not exist.");    }

    private static void ValidateRelativePath(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (Path.IsPathRooted(relative) ||
            normalized.StartsWith('/') ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Contains(':'))
            throw new InvalidOperationException("Unsafe staged update path.");

        var first = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        if (new[]
            {
                "pharmacy.db",
                "backups",
                "config",
                "licensing",
                "certificates",
                "logs",
                "network.json",
                "network-secrets.bin",
            }.Contains(first, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Staged update contains a protected pharmacy data path.");
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
        }
    }

    private static async Task<bool> StopWindowsServiceAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var status = await RunAsync(
            "sc.exe",
            $"query \"{serviceName}\"",
            cancellationToken);
        if (!status.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return false;

        var stop = await RunAsync(
            "sc.exe",
            $"stop \"{serviceName}\"",
            cancellationToken);
        if (stop.ExitCode != 0)
            throw new InvalidOperationException(
                "Could not stop the Darmaltoon Local Server for update.");

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            status = await RunAsync(
                "sc.exe",
                $"query \"{serviceName}\"",
                cancellationToken);            if (!status.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) &&
                !status.Output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        throw new InvalidOperationException(
            "The Darmaltoon Local Server did not stop in time.");
    }

    private static async Task<bool> StartWindowsServiceAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "sc.exe",
            $"start \"{serviceName}\"",
            cancellationToken);
        return result.ExitCode == 0 ||
               result.Output.Contains("already running", StringComparison.OrdinalIgnoreCase);
    }

    private static void RestartApplication(UpdateApplyPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.RestartExecutable))
            return;

        var executable = Path.GetFullPath(plan.RestartExecutable);
        if (!UpdateService.IsSameOrChild(executable, plan.InstallationDirectory) ||
            !File.Exists(executable))
            return;

        _ = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = plan.InstallationDirectory,
        });
    }    private static async Task WriteSuccessMarkerAsync(
        UpdateApplyPlan plan,
        CancellationToken cancellationToken)
    {
        var marker = Path.Combine(
            Path.GetDirectoryName(plan.RollbackDirectory)!,
            "last-applied.json");

        await File.WriteAllTextAsync(
            marker,
            JsonSerializer.Serialize(
                new
                {
                    version = plan.TargetVersion,
                    applied_at = DateTimeOffset.UtcNow,
                    mode = plan.DeploymentMode,
                    pre_update_backup = plan.PreUpdateBackupPath,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }),
            cancellationToken);
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

        process.Start();        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new(
            process.ExitCode,
            (await stdout) + Environment.NewLine + (await stderr));
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
