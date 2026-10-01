using System.Security.Cryptography;
using System.Text;

namespace BusinessOS.Pharmacy.Updater;

public static class UpdateManifestSecurity
{
    public const int SupportedSchemaVersion = 1;
    public const string SupportedApiVersion = "v1";

    public static void ValidateAndVerify(UpdateManifest manifest, string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException("The update manifest schema is not supported by this Darmaltoon version.");
        if (!string.Equals(manifest.ApiVersion, SupportedApiVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update requires an incompatible local LAN API version.");
        if (!Version.TryParse(manifest.Version, out _)) throw new InvalidOperationException("Update version is invalid.");
        if (!Version.TryParse(manifest.MinimumSupportedVersion, out _)) throw new InvalidOperationException("Minimum supported version is invalid.");
        if (manifest.MinimumServerVersion is not null && !Version.TryParse(manifest.MinimumServerVersion, out _))
            throw new InvalidOperationException("Minimum server version is invalid.");
        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) ||
            packageUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(packageUri.UserInfo) ||
            !string.IsNullOrEmpty(packageUri.Fragment))
            throw new InvalidOperationException(
                "Update packages must use HTTPS without embedded credentials or fragments.");
        if (manifest.PackageSha256.Length != 64 || !manifest.PackageSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Update package checksum is invalid.");
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            throw new InvalidOperationException("No trusted Darmaltoon update signing key is configured.");

        byte[] signature;
        try { signature = Convert.FromBase64String(manifest.Signature); }
        catch (FormatException) { throw new InvalidOperationException("Update manifest signature is invalid."); }

        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(publicKeyPem); }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        { throw new InvalidOperationException("The trusted update signing key is invalid.", ex); }

        var payload = Encoding.UTF8.GetBytes(CanonicalPayload(manifest));
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidOperationException("Update manifest signature verification failed.");
    }

    public static string CanonicalPayload(UpdateManifest manifest) => string.Join("\n", new[]
    {
        manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        manifest.Channel.Trim().ToLowerInvariant(),
        manifest.Version.Trim(),
        manifest.ApiVersion.Trim().ToLowerInvariant(),
        manifest.MinimumSupportedVersion.Trim(),
        manifest.MinimumServerVersion?.Trim() ?? string.Empty,
        manifest.PackageUrl.Trim(),
        manifest.PackageSha256.Trim().ToUpperInvariant(),
        manifest.PublishedAt.ToUniversalTime().ToString("O"),
        manifest.ReleaseNotes ?? string.Empty,
    });
}
