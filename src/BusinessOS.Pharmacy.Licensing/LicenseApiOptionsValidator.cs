using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class LicenseApiOptionsValidator : IValidateOptions<LicenseApiOptions>
{
    public ValidateOptionsResult Validate(string? name, LicenseApiOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return ValidateOptionsResult.Fail("BusinessOS:Licensing:BaseUrl must be an absolute URL.");
        }

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !baseUri.IsLoopback)
        {
            return ValidateOptionsResult.Fail("The licensing service must use HTTPS except for loopback development endpoints.");
        }

        if (!IsApiPath(options.ActivationPath) || !IsApiPath(options.RefreshPath))
        {
            return ValidateOptionsResult.Fail("Licensing API paths must start with /api/.");
        }

        if (options.TimeoutSeconds is < 3 or > 120)
        {
            return ValidateOptionsResult.Fail("BusinessOS:Licensing:TimeoutSeconds must be between 3 and 120.");
        }

        if (options.ClockRollbackToleranceMinutes is < 1 or > 120)
        {
            return ValidateOptionsResult.Fail("BusinessOS:Licensing:ClockRollbackToleranceMinutes must be between 1 and 120.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningPublicKey))
        {
            return ValidateOptionsResult.Fail("BusinessOS:Licensing:SigningPublicKey must be configured for signed entitlement verification.");
        }

        try
        {
            if (Convert.FromBase64String(options.SigningPublicKey).Length != 32)
            {
                return ValidateOptionsResult.Fail("BusinessOS:Licensing:SigningPublicKey must be a 32-byte Ed25519 public key.");
            }
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail("BusinessOS:Licensing:SigningPublicKey must be valid Base64.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsApiPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith("/api/", StringComparison.Ordinal);
}