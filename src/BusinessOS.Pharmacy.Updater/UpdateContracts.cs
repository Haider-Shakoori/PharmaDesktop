using System.Text.Json.Serialization;

namespace BusinessOS.Pharmacy.Updater;

public sealed record UpdateOptions(
    string ManifestUrl,
    string SigningPublicKeyPem,
    int TimeoutSeconds = 30,
    UpdateChannel Channel = UpdateChannel.Stable,
    long MaximumPackageBytes = 536_870_912);

public sealed record UpdateManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("minimum_supported_version")] string MinimumSupportedVersion,
    [property: JsonPropertyName("minimum_server_version")] string? MinimumServerVersion,
    [property: JsonPropertyName("package_url")] string PackageUrl,
    [property: JsonPropertyName("package_sha256")] string PackageSha256,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("release_notes")] string? ReleaseNotes,
    [property: JsonPropertyName("signature")] string Signature);

public enum UpdateAvailability
{
    UpToDate = 0,
    Available = 1,
    Required = 2,
    Blocked = 3,
}

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    Version CurrentVersion,
    Version? AvailableVersion,
    UpdateManifest? Manifest,
    string Message);

public sealed record PreparedUpdate(
    UpdateManifest Manifest,
    string PackagePath,
    string StagingDirectory,
    DateTimeOffset PreparedAt);

public sealed record UpdateApplyPlan(
    int SchemaVersion,
    string TargetVersion,
    string InstallationDirectory,
    string DataRootDirectory,
    string StagingDirectory,
    string RollbackDirectory,
    string DeploymentMode,
    int WaitForProcessId,
    string? RestartExecutable,
    string? LocalServerServiceName,
    string? PreUpdateBackupPath,
    DateTimeOffset CreatedAt);

public sealed record UpdateApplyResult(bool Success, string Message, string? RollbackDirectory = null);
