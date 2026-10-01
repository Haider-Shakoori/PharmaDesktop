using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Updater;

public sealed class UpdateService
{
    private readonly HttpClient _http;
    private readonly UpdateOptions _options;
    private readonly string _updatesRoot;

    public UpdateService(HttpClient http, UpdateOptions options, string updatesRoot)
    {
        _http = http;
        _options = options;
        if (options.TimeoutSeconds is < 5 or > 300)
            throw new ArgumentOutOfRangeException(nameof(options), "Update timeout must be between 5 and 300 seconds.");
        if (options.MaximumPackageBytes is < 1_048_576 or > 2_147_483_648L)
            throw new ArgumentOutOfRangeException(nameof(options), "Update package limit must be between 1 MiB and 2 GiB.");
        _updatesRoot = Path.GetFullPath(updatesRoot);
        ValidateManifestUrl(options.ManifestUrl);
        Directory.CreateDirectory(_updatesRoot);
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        string deploymentMode,
        Version? mainServerVersion = null,
        CancellationToken cancellationToken = default)
    {        if (string.IsNullOrWhiteSpace(_options.ManifestUrl))
            return new(UpdateAvailability.Blocked, currentVersion, null, null, "Update manifest URL is not configured.");
        if (string.IsNullOrWhiteSpace(_options.SigningPublicKeyPem))
            return new(UpdateAvailability.Blocked, currentVersion, null, null, "Trusted update signing key is not configured.");

        var uri = BuildManifestUri(currentVersion, deploymentMode, mainServerVersion);
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long manifestLength && manifestLength > 1_048_576)
            throw new InvalidOperationException("The update manifest response is too large.");
        var manifest = await ReadManifestAsync(response.Content, cancellationToken);

        UpdateManifestSecurity.ValidateAndVerify(manifest, _options.SigningPublicKeyPem);
        if (!string.Equals(manifest.Channel, _options.Channel.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update manifest channel does not match this installation.");

        var available = Version.Parse(manifest.Version);
        var minimum = Version.Parse(manifest.MinimumSupportedVersion);
        if (available <= currentVersion)
            return new(UpdateAvailability.UpToDate, currentVersion, available, manifest, "Darmaltoon is up to date.");

        if (string.Equals(deploymentMode, "Client", StringComparison.OrdinalIgnoreCase) &&
            manifest.MinimumServerVersion is { Length: > 0 } minServer)
        {            if (mainServerVersion is null)
                return new(UpdateAvailability.Blocked, currentVersion, available, manifest,
                    "Main Pharmacy Server version could not be verified. Client update was blocked.");
            if (mainServerVersion < Version.Parse(minServer))
                return new(UpdateAvailability.Blocked, currentVersion, available, manifest,
                    $"Update the Main Pharmacy Server to {minServer} or newer before updating this Client Terminal.");
        }

        var availability = currentVersion < minimum
            ? UpdateAvailability.Required
            : UpdateAvailability.Available;
        return new(availability, currentVersion, available, manifest,
            availability == UpdateAvailability.Required
                ? "This Darmaltoon version is below the supported minimum and should be updated."
                : "A Darmaltoon update is available.");
    }

    public async Task<PreparedUpdate> DownloadAndStageAsync(
        UpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        UpdateManifestSecurity.ValidateAndVerify(manifest, _options.SigningPublicKeyPem);
        var version = Version.Parse(manifest.Version).ToString();
        var versionRoot = SafeChild(_updatesRoot, version);
        var downloads = SafeChild(versionRoot, "download");
        var staging = SafeChild(versionRoot, "staging");
        Directory.CreateDirectory(downloads);        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var packagePath = SafeChild(downloads, "darmaltoon-update.zip");
        var partial = packagePath + ".partial";
        if (File.Exists(partial))
            File.Delete(partial);

        using (var response = await _http.GetAsync(
                   manifest.PackageUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length &&
                length > _options.MaximumPackageBytes)
                throw new InvalidOperationException("Update package is larger than the allowed maximum size.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long total = 0;
            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, 81920), cancellationToken);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > _options.MaximumPackageBytes)
                        throw new InvalidOperationException("Update package is larger than the allowed maximum size.");

                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }

            await target.FlushAsync(cancellationToken);
            var hash = Convert.ToHexString(sha.GetHashAndReset());
            if (!string.Equals(hash, manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update package checksum verification failed.");
        }

        File.Move(partial, packagePath, overwrite: true);
        ExtractValidated(packagePath, staging);
        await File.WriteAllTextAsync(
            SafeChild(versionRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken);
        return new(manifest, packagePath, staging, DateTimeOffset.UtcNow);
    }    public async Task<string> WriteApplyPlanAsync(
        PreparedUpdate prepared,
        string installationDirectory,
        string dataRootDirectory,
        string deploymentMode,
        int waitForProcessId,
        string? restartExecutable,
        string? localServerServiceName,
        string? preUpdateBackupPath,
        CancellationToken cancellationToken = default)
    {
        installationDirectory = Path.GetFullPath(installationDirectory);
        dataRootDirectory = Path.GetFullPath(dataRootDirectory);

        if (IsSameOrChild(dataRootDirectory, installationDirectory) ||
            IsSameOrChild(installationDirectory, dataRootDirectory))
            throw new InvalidOperationException(
                "Application installation and pharmacy data directories must be isolated before update.");

        if (!IsSameOrChild(prepared.StagingDirectory, _updatesRoot))
            throw new InvalidOperationException("Update staging directory is outside the trusted update workspace.");

        var rollback = SafeChild(
            _updatesRoot,
            Path.Combine(prepared.Manifest.Version, "rollback-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")));

        var plan = new UpdateApplyPlan(
            1, prepared.Manifest.Version, installationDirectory, dataRootDirectory,
            prepared.StagingDirectory, rollback, deploymentMode, waitForProcessId,
            restartExecutable, localServerServiceName, preUpdateBackupPath, DateTimeOffset.UtcNow);        var planPath = SafeChild(
            _updatesRoot,
            Path.Combine(prepared.Manifest.Version, "apply-plan.json"));
        await File.WriteAllTextAsync(
            planPath,
            JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken);
        return planPath;
    }

    public Process LaunchApplyAgent(string planPath)
    {
        var runner = PrepareRunner();
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows() &&
            runner.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            start = new ProcessStartInfo(runner, $"--apply \"{planPath}\"")
            {
                UseShellExecute = true,
            };
        }
        else
        {
            start = new ProcessStartInfo("dotnet", $"\"{runner}\" --apply \"{planPath}\"")
            {
                UseShellExecute = false,
            };
        }

        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the Darmaltoon update agent.");
    }    private static void ValidateManifestUrl(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return;

        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException(
                "The update manifest endpoint must be HTTPS without embedded credentials or a fragment.");
    }

    private static async Task<UpdateManifest> ReadManifestAsync(HttpContent content, CancellationToken cancellationToken)
    {
        const int maximumBytes = 1_048_576;
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(16_384);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await source.ReadAsync(rented.AsMemory(0, 16_384), cancellationToken);
                if (read == 0)
                    break;
                total += read;
                if (total > maximumBytes)
                    throw new InvalidOperationException("The update manifest response is too large.");
                await buffer.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(
                   buffer,
                   new JsonSerializerOptions(JsonSerializerDefaults.Web),
                   cancellationToken)
               ?? throw new InvalidOperationException("The update service returned an empty manifest.");
    }

    private Uri BuildManifestUri(
        Version currentVersion,
        string deploymentMode,
        Version? serverVersion)
    {
        var separator = _options.ManifestUrl.Contains('?') ? '&' : '?';
        var url = _options.ManifestUrl + separator +
                  "channel=" + Uri.EscapeDataString(_options.Channel.ToString().ToLowerInvariant()) +
                  "&current=" + Uri.EscapeDataString(currentVersion.ToString()) +
                  "&mode=" + Uri.EscapeDataString(deploymentMode);

        if (serverVersion is not null)
            url += "&server_version=" + Uri.EscapeDataString(serverVersion.ToString());

        return new Uri(url, UriKind.Absolute);
    }

    private string PrepareRunner()
    {
        var sourceDll = typeof(UpdateService).Assembly.Location;
        var selfContainedUpdater = Path.Combine(AppContext.BaseDirectory, "Updater");
        var sourceDir = Directory.Exists(selfContainedUpdater)
            ? selfContainedUpdater
            : Path.GetDirectoryName(sourceDll)!;
        var runnerDir = SafeChild(
            _updatesRoot,
            Path.Combine("runner", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(runnerDir);

        foreach (var source in Directory.EnumerateFiles(
                     sourceDir, "BusinessOS.Pharmacy.Updater*"))
            File.Copy(source, Path.Combine(runnerDir, Path.GetFileName(source)), overwrite: true);        var executable = Path.Combine(runnerDir, "BusinessOS.Pharmacy.Updater.exe");
        var dll = Path.Combine(runnerDir, "BusinessOS.Pharmacy.Updater.dll");
        var runner = OperatingSystem.IsWindows() && File.Exists(executable)
            ? executable
            : dll;

        if (!File.Exists(runner))
            throw new InvalidOperationException(
                "Update agent runtime files are missing from the application installation.");

        return runner;
    }

    private void ExtractValidated(string packagePath, string staging)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > 10_000)
            throw new InvalidOperationException("Update package contains too many files.");

        var expandedLimit = _options.MaximumPackageBytes > long.MaxValue / 4
            ? long.MaxValue
            : Math.Min(_options.MaximumPackageBytes * 4, 2_147_483_648L);
        long expandedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.EndsWith('/'))
                continue;

            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidOperationException("Update packages may not contain symbolic links.");
            if (entry.Length > expandedLimit - expandedBytes)
                throw new InvalidOperationException("Update package expands beyond the allowed maximum size.");
            expandedBytes += entry.Length;

            if (normalized.StartsWith('/') ||
                normalized.Contains("../", StringComparison.Ordinal) ||
                normalized.Contains(':'))
                throw new InvalidOperationException(
                    $"Unsafe update package path: {entry.FullName}");

            var first = normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;            if (ProtectedNames.Contains(first))
                throw new InvalidOperationException(
                    $"Update package attempts to include protected pharmacy data path: {first}");

            var target = Path.GetFullPath(Path.Combine(staging, normalized));
            if (!IsSameOrChild(target, staging))
                throw new InvalidOperationException(
                    "Update package path escapes the staging directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static readonly HashSet<string> ProtectedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "pharmacy.db",
            "backups",
            "config",
            "licensing",
            "certificates",
            "logs",
            "network.json",
            "network-secrets.bin",
        };

    private static string SafeChild(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));        if (!IsSameOrChild(path, root))
            throw new InvalidOperationException("Unsafe update workspace path.");
        return path;
    }

    internal static bool IsSameOrChild(string path, string root)
    {
        path = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(
                   root + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
