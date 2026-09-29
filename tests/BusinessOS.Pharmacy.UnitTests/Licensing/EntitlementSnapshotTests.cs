using BusinessOS.Pharmacy.Domain.Licensing;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Licensing;

public sealed class EntitlementSnapshotTests
{
    [Fact]
    public void OfflineLease_IsValid_OnlyInsideSignedWindow()
    {
        var issuedAt = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
        var entitlement = Create(issuedAt, issuedAt.AddDays(7));

        Assert.True(entitlement.IsOfflineLeaseValidAt(issuedAt.AddDays(1)));
        Assert.False(entitlement.IsOfflineLeaseValidAt(issuedAt.AddSeconds(-1)));
        Assert.False(entitlement.IsOfflineLeaseValidAt(issuedAt.AddDays(7)));
    }

    [Fact]
    public void FeatureLookup_IsCentralized()
    {
        var entitlement = Create(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "advanced_reports" });

        Assert.True(entitlement.HasFeature("ADVANCED_REPORTS"));
        Assert.False(entitlement.HasFeature("cloud_backup"));
    }

    private static EntitlementSnapshot Create(
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        IReadOnlySet<string>? features = null) =>
        new(
            "tenant-1",
            "subscription-1",
            "license-1",
            1,
            "activation-1",
            "device-1",
            "TRIAL",
            SubscriptionState.Trial,
            issuedAt,
            expiresAt,
            features ?? new HashSet<string>());
}
