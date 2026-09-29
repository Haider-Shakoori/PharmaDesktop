using BusinessOS.Pharmacy.Domain.Licensing;

namespace BusinessOS.Pharmacy.Licensing;

public sealed record ActivationState(
    string LeaseToken,
    string DeviceId,
    DateTimeOffset LastServerTime,
    DateTimeOffset LastTrustedLocalTime,
    EntitlementSnapshot Entitlement,
    TenantSummary? Tenant = null,
    PlanSummary? Plan = null,
    string? SubscriptionHealth = null);

public interface IActivationStore
{
    Task<ActivationState?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ActivationState state, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
