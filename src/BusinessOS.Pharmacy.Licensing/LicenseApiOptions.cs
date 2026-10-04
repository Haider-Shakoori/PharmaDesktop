namespace BusinessOS.Pharmacy.Licensing;

public sealed class LicenseApiOptions
{
    public const string SectionName = "BusinessOS:Licensing";

    public string BaseUrl { get; init; } = "https://pharmacy.darmaltoon.com";
    public string ActivationPath { get; init; } = "/api/v1/license/activate";
    public string RefreshPath { get; init; } = "/api/v1/desktop/license/refresh";
    public string SessionLoginPath { get; init; } = "/api/v1/desktop/session/login";
    public string SessionRefreshPath { get; init; } = "/api/v1/desktop/session/refresh";
    public string SigningPublicKey { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 15;
    public int ClockRollbackToleranceMinutes { get; init; } = 10;
}
