namespace BusinessOS.Pharmacy.Domain.Licensing;

public sealed record EntitlementSnapshot(
    string TenantId,
    string SubscriptionId,
    string LicenseId,
    int LicenseVersion,
    string ActivationId,
    string DeviceId,
    string PlanCode,
    SubscriptionState SubscriptionState,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlySet<string> Features,
    DateTimeOffset? TrialStartedAt = null,
    DateTimeOffset? TrialExpiresAt = null,
    DateTimeOffset? SubscriptionExpiresAt = null)
{
    public bool IsOfflineLeaseValidAt(DateTimeOffset trustedNow) =>
        trustedNow >= IssuedAt && trustedNow < ExpiresAt;

    public bool HasFeature(string feature) =>
        !string.IsNullOrWhiteSpace(feature) && Features.Contains(feature);
}
