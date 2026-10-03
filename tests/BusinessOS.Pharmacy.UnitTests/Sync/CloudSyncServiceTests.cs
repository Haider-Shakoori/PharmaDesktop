using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Domain.Licensing;
using BusinessOS.Pharmacy.Licensing;
using BusinessOS.Pharmacy.Sync;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Sync;

public sealed class CloudSyncServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Client_terminal_never_contacts_cloud()
    {
        var store = new FakeStore();
        var transport = new FakeTransport();
        var service = CreateService(
            DeploymentMode.Client,
            store,
            transport,
            user: null,
            entitlement: null,
            protectedSession: null);

        var result = await service.SyncOnceAsync();

        Assert.Equal(
            CloudSyncRunState.DisabledForClientTerminal,
            result.State);
        Assert.Equal(0, transport.PushCalls);
        Assert.Equal(0, transport.PullCalls);
    }

    [Fact]
    public async Task Retryable_transport_failure_defers_outbox_without_losing_event()
    {
        var user = User();
        var entitlement = Entitlement();
        var store = new FakeStore
        {
            Pending =
            [
                new CloudSyncOutboxItem(
                    "outbox-1",
                    "tenant-1",
                    "user-1",
                    "sale.completed",
                    "sale:offline:1",
                    """{"cashier_user_id":"user-1"}""",
                    0,
                    Now.AddMinutes(-1)),
            ],
        };

        var transport = new FakeTransport
        {
            PushFailure = new CloudSyncTransportException(
                "network unavailable",
                retryable: true),
        };

        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            transport,
            user,
            entitlement,
            ProtectedSession(user));

        var result = await service.SyncOnceAsync();

        Assert.Equal(CloudSyncRunState.Offline, result.State);
        Assert.Single(store.DeferredKeys);
        Assert.Equal("sale:offline:1", store.DeferredKeys[0]);
        Assert.Empty(store.AcceptedKeys);
        Assert.Empty(store.RejectedKeys);
        Assert.Equal(1, transport.PushCalls);
        Assert.Equal(0, transport.PullCalls);
    }

    [Fact]
    public async Task Tenant_identity_mismatch_blocks_cloud_before_transport()
    {
        var user = User() with { TenantId = "tenant-other" };
        var entitlement = Entitlement();

        var store = new FakeStore();
        var transport = new FakeTransport();
        var service = CreateService(
            DeploymentMode.Server,
            store,
            transport,
            user,
            entitlement,
            ProtectedSession(user));

        var result = await service.SyncOnceAsync();

        Assert.Equal(CloudSyncRunState.Failed, result.State);
        Assert.Equal(0, transport.PushCalls);
        Assert.Equal(0, transport.PullCalls);
    }

    [Fact]
    public async Task Signed_in_sync_repairs_legacy_reference_conflicts_before_cloud_work()
    {
        var user = User();
        var store = new FakeStore();
        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            new FakeTransport(),
            user,
            Entitlement(),
            ProtectedSession(user));

        var result = await service.SyncOnceAsync();

        Assert.Equal(CloudSyncRunState.Synced, result.State);
        Assert.Equal(1, store.RepairCalls);
    }

    [Fact]
    public async Task Conflict_review_lists_retained_conflicts_for_signed_in_cashier()
    {
        var user = User();
        var store = new FakeStore();
        store.Conflicts.Add(Conflict("sale:1", "user-1"));
        store.Conflicts.Add(Conflict("sale:2", "user-1"));

        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            new FakeTransport(),
            user,
            Entitlement(),
            ProtectedSession(user));

        var review = await service.GetConflictReviewAsync();

        Assert.Equal(2, review.Conflicts.Count);
        Assert.Contains("2", review.Message);
        Assert.Equal("sale:1", review.Conflicts[0].IdempotencyKey);
        Assert.Equal("record-1", review.Conflicts[0].LocalId);
        Assert.Equal("2026-10-01", review.Conflicts[0].BusinessDate);
    }

    [Fact]
    public async Task Retrying_a_conflict_requeues_it_for_the_next_sync_run()
    {
        var user = User();
        var store = new FakeStore();
        store.Conflicts.Add(Conflict("sale:1", "user-1"));

        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            new FakeTransport(),
            user,
            Entitlement(),
            ProtectedSession(user));

        var review = await service.RetryConflictAsync("sale:1");

        Assert.Empty(review.Conflicts);
        Assert.Empty(store.Conflicts);
    }

    [Fact]
    public async Task Conflict_retry_is_blocked_for_another_cashiers_event()
    {
        var user = User();
        var store = new FakeStore();
        store.Conflicts.Add(Conflict("sale:1", "user-2"));

        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            new FakeTransport(),
            user,
            Entitlement(),
            ProtectedSession(user));

        var review = await service.RetryConflictAsync("sale:1");

        Assert.Single(review.Conflicts);
        Assert.Single(store.Conflicts);
        Assert.Contains("another cashier", review.Message);
    }

    [Fact]
    public async Task Dismissing_a_conflict_clears_it_without_touching_pending_events()
    {
        var user = User();
        var store = new FakeStore
        {
            Pending =
            [
                new CloudSyncOutboxItem(
                    "outbox-1",
                    "tenant-1",
                    "user-1",
                    "sale.completed",
                    "sale:2",
                    """{"local_id":"record-2"}""",
                    0,
                    Now.AddMinutes(-1)),
            ],
            Conflicts =
            {
                Conflict("sale:1", "user-1"),
            },
        };

        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            new FakeTransport(),
            user,
            Entitlement(),
            ProtectedSession(user));

        var review = await service.DismissConflictAsync("sale:1");

        Assert.Empty(review.Conflicts);
        Assert.Single(store.Pending);
    }

    [Fact]
    public async Task Conflict_review_requires_a_signed_in_pharmacy_user()
    {
        var store = new FakeStore();
        store.Conflicts.Add(Conflict("sale:1", "user-1"));

        var service = CreateService(
            DeploymentMode.Standalone,
            store,
            new FakeTransport(),
            user: null,
            entitlement: null,
            protectedSession: null);

        var review = await service.GetConflictReviewAsync();

        Assert.Empty(review.Conflicts);
        Assert.Contains("Sign in", review.Message);
    }

    private static CloudSyncConflictItem Conflict(string key, string actorUserId) =>
        new(
            key,
            "sale.completed",
            actorUserId,
            "record-1",
            "2026-10-01",
            "reference_missing",
            "A referenced pharmacy record no longer exists.",
            1,
            Now.AddHours(-2),
            CanRetry: true);

    private static CloudSyncService CreateService(
        DeploymentMode mode,
        FakeStore store,
        FakeTransport transport,
        UserSessionSnapshot? user,
        EntitlementSnapshot? entitlement,
        DesktopSessionState? protectedSession)
    {
        return new CloudSyncService(
            store,
            transport,
            new FakeLicenseService(entitlement),
            new FakeUserSessionService(user),
            new FakeSessionStore(protectedSession),
            new FakeClock(Now),
            new NetworkConfiguration
            {
                Mode = mode,
                IsConfigured = mode != DeploymentMode.Client,
            },
            new CloudSyncOptions(
                "https://pharmacy.businessos.af",
                IntervalSeconds: 30));
    }

    private static EntitlementSnapshot Entitlement() =>
        new(
            "tenant-1",
            "subscription-1",
            "license-1",
            1,
            "activation-1",
            "device-1",
            "STANDARD",
            SubscriptionState.Active,
            Now.AddHours(-1),
            Now.AddDays(7),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "desktop_sync",
            });

    private static UserSessionSnapshot User() =>
        new(
            "user-1",
            "tenant-1",
            "activation-1",
            "device-1",
            "Cashier",
            "cashier@example.test",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cashier",
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pos.sell",
            },
            Now.AddMinutes(-5),
            Now.AddHours(8));

    private static DesktopSessionState ProtectedSession(
        UserSessionSnapshot user) =>
        new(
            "protected-desktop-access-token",
            user,
            Now,
            Now,
            null);

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeLicenseService(
        EntitlementSnapshot? entitlement) : ILicenseService
    {
        public Task<EntitlementSnapshot?> GetCachedEntitlementAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entitlement);

        public Task<EntitlementSnapshot> ActivateAsync(
            string licenseKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EntitlementSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUserSessionService(
        UserSessionSnapshot? current) : IUserSessionService
    {
        public UserSessionSnapshot? Current { get; } = current;

        public Task<UserSessionSnapshot> LoginAsync(
            string email,
            string password,
            bool allowOfflineSignIn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UserSessionSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LogoutAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSessionStore(
        DesktopSessionState? state) : IUserSessionStore
    {
        public Task<DesktopSessionState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(state);

        public Task SaveAsync(
            DesktopSessionState value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeTransport : ICloudSyncTransport
    {
        public int PushCalls { get; private set; }
        public int PullCalls { get; private set; }
        public Exception? PushFailure { get; init; }

        public Task<IReadOnlyList<CloudSyncPushAcknowledgement>> PushAsync(
            string accessToken,
            IReadOnlyList<CloudSyncOutboxItem> events,
            CancellationToken cancellationToken = default)
        {
            PushCalls++;

            if (PushFailure is not null)
                return Task.FromException<
                    IReadOnlyList<CloudSyncPushAcknowledgement>>(
                    PushFailure);

            return Task.FromResult<
                IReadOnlyList<CloudSyncPushAcknowledgement>>([]);
        }

        public Task<CloudSyncPullPage> PullAsync(
            string accessToken,
            string stream,
            string? cursor,
            int limit,
            CancellationToken cancellationToken = default)
        {
            PullCalls++;
            return Task.FromResult(
                new CloudSyncPullPage(
                    stream,
                    [],
                    cursor,
                    false,
                    Now));
        }
    }

    private sealed class FakeStore : ICloudSyncStore
    {
        public IReadOnlyList<CloudSyncOutboxItem> Pending { get; init; } = [];
        public List<string> AcceptedKeys { get; } = [];
        public List<string> RejectedKeys { get; } = [];
        public List<string> DeferredKeys { get; } = [];
        public List<CloudSyncConflictItem> Conflicts { get; } = [];
        public int RepairCalls { get; private set; }

        public Task<IReadOnlyList<CloudSyncOutboxItem>> GetPendingAsync(
            string tenantId,
            string actorUserId,
            int take,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Pending);

        public Task<int> RepairReferenceConflictsAsync(
            string tenantId,
            string actorUserId,
            CancellationToken cancellationToken = default)
        {
            RepairCalls++;
            return Task.FromResult(0);
        }

        public Task MarkAcceptedAsync(
            string tenantId,
            string idempotencyKey,
            string? serverId,
            DateTimeOffset? serverUpdatedAt,
            CancellationToken cancellationToken = default)
        {
            AcceptedKeys.Add(idempotencyKey);
            return Task.CompletedTask;
        }

        public Task MarkRejectedAsync(
            string tenantId,
            string idempotencyKey,
            string code,
            string message,
            bool retryable,
            DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            RejectedKeys.Add(idempotencyKey);
            return Task.CompletedTask;
        }

        public Task DeferAsync(
            string tenantId,
            IReadOnlyCollection<string> idempotencyKeys,
            string message,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            DeferredKeys.AddRange(idempotencyKeys);
            return Task.CompletedTask;
        }

        public Task<string?> GetCursorAsync(
            string tenantId,
            string stream,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SaveRemotePageAsync(
            string tenantId,
            string stream,
            string? nextCursor,
            IReadOnlyList<CloudSyncRemoteRecord> records,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CloudSyncQueueSnapshot> GetQueueSnapshotAsync(
            string tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new CloudSyncQueueSnapshot(
                    Pending.Count,
                    Conflicts.Count,
                    0,
                    Pending.Count == 0
                        ? null
                        : Pending.Min(x => x.CreatedAt)));

        public Task<IReadOnlyList<CloudSyncConflictItem>> GetConflictsAsync(
            string tenantId,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudSyncConflictItem>>(
                Conflicts.Take(Math.Clamp(take, 1, 200)).ToList());

        public Task<bool> RetryConflictAsync(
            string tenantId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var index = Conflicts.FindIndex(
                x => string.Equals(
                    x.IdempotencyKey,
                    idempotencyKey,
                    StringComparison.Ordinal));

            if (index < 0)
                return Task.FromResult(false);

            Conflicts.RemoveAt(index);
            return Task.FromResult(true);
        }

        public Task<bool> DismissConflictAsync(
            string tenantId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            RetryConflictAsync(tenantId, idempotencyKey, cancellationToken);
    }
}
