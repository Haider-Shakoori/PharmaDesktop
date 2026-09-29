namespace BusinessOS.Pharmacy.Licensing;

public sealed class LicenseApiOptions
{
    public const string SectionName = "BusinessOS:Licensing";

    public string BaseUrl { get; init; } = "https://pharmacy.businessos.af";
    public string ActivationPath { get; init; } = "/api/v1/license/activate";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}
