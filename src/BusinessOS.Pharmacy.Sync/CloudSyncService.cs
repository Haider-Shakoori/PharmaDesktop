using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Licensing;

namespace BusinessOS.Pharmacy.Sync;

public sealed class CloudSyncService : ICloudSyncService
{
    private static readonly string[] PullStreams =
        ["medicines", "customers", "inventory"];

    private readonly ICloudSyncStore _store;
    private readonly ICloudSyncTransport _transport;
    private readonly ILicenseService _licenses;
    private readonly IUserSessionService _sessions;
    private readonly IUserSessionStore _sessionStore;
    private readonly IClock _clock;
    private readonly NetworkConfiguration _network;
    private readonly CloudSyncOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CloudSyncService(
        ICloudSyncStore store,
        ICloudSyncTransport transport,
        ILicenseService licenses,
        IUserSessionService sessions,
        IUserSessionStore sessionStore,
        IClock clock,
        NetworkConfiguration network,
        CloudSyncOptions options)
    {
        _store = store;
        _transport = transport;
        _licenses = licenses;
        _sessions = sessions;
        _sessionStore = sessionStore;
        _clock = clock;
        _network = network;
        _options = options;
    }

    public CloudSyncRunResult LastResult { get; private set; } =
        CloudSyncRunResult.Initial;

    public event Action<CloudSyncRunResult>? ResultUpdated;

    public async Task<CloudSyncRunResult> SyncOnceAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LastResult = await RunCoreAsync(cancellationToken);
            ResultUpdated?.Invoke(LastResult);
            return LastResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CloudSyncRunResult> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (_network.Mode == DeploymentMode.Client)
        {
            return new(
                CloudSyncRunState.DisabledForClientTerminal,
                "Client Terminals synchronize through the Main Pharmacy Server, not directly with BusinessOS cloud.",
                AttemptedAt: now);
        }

        var user = _sessions.Current;
        if (user is null)
        {
            return new(
                CloudSyncRunState.WaitingForSignIn,
                "Cloud synchronization is waiting for a pharmacy user to sign in.",
                AttemptedAt: now);
        }

        if (!user.IsValidAt(now))
        {
            return new(
                CloudSyncRunState.WaitingForSignIn,
                "The desktop user session has expired. Sign in online to resume cloud synchronization.",
                AttemptedAt: now);
        }

        var entitlement = await GetEntitlementAsync(cancellationToken);
        if (entitlement is null)
        {
            return new(
                CloudSyncRunState.LicenseRejected,
                "The signed pharmacy subscription lease must be verified before cloud synchronization can continue.",
                AttemptedAt: now);
        }

        if (!string.Equals(
                user.TenantId,
                entitlement.TenantId,
                StringComparison.Ordinal) ||
            !string.Equals(
                user.ActivationId,
                entitlement.ActivationId,
                StringComparison.Ordinal) ||
            !string.Equals(
                user.DeviceId,
                entitlement.DeviceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                CloudSyncRunState.Failed,
                "Cloud synchronization was blocked because the signed user and pharmacy activation identities do not match.",
                AttemptedAt: now);
        }

        DesktopSessionState? protectedSession;
        try
        {
            protectedSession =
                await _sessionStore.LoadAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            return new(
                CloudSyncRunState.Failed,
                $"The protected desktop cloud session could not be read: {exception.Message}",
                AttemptedAt: now);
        }

        if (protectedSession is null ||
            string.IsNullOrWhiteSpace(protectedSession.AccessToken))
        {
            return new(
                CloudSyncRunState.WaitingForSignIn,
                "Cloud synchronization is waiting for an online desktop sign-in.",
                AttemptedAt: now);
        }

        if (!string.Equals(
                protectedSession.User.TenantId,
                entitlement.TenantId,
                StringComparison.Ordinal) ||
            !string.Equals(
                protectedSession.User.UserId,
                user.UserId,
                StringComparison.Ordinal) ||
            !string.Equals(
                protectedSession.User.ActivationId,
                entitlement.ActivationId,
                StringComparison.Ordinal))
        {
            return new(
                CloudSyncRunState.Failed,
                "The protected desktop cloud session belongs to a different pharmacy identity.",
                AttemptedAt: now);
        }

        var accessToken = protectedSession.AccessToken;
        var pushed = 0;
        var pulled = 0;
        var runConflicts = 0;

        await _store.RepairReferenceConflictsAsync(
            entitlement.TenantId,
            user.UserId,
            cancellationToken);

        var pending = await _store.GetPendingAsync(
            entitlement.TenantId,
            user.UserId,
            _options.BatchSize,
            now,
            cancellationToken);

        if (pending.Count > 0)
        {
            IReadOnlyList<CloudSyncPushAcknowledgement> acknowledgements;
            try
            {
                acknowledgements = await _transport.PushAsync(
                    accessToken,
                    pending,
                    cancellationToken);
            }
            catch (CloudSyncAuthorizationException exception)
            {
                return new(
                    CloudSyncRunState.LicenseRejected,
                    exception.Message,
                    AttemptedAt: now);
            }
            catch (CloudSyncTransportException exception)
            {
                if (exception.Retryable)
                {
                    await _store.DeferAsync(
                        entitlement.TenantId,
                        pending.Select(x => x.IdempotencyKey).ToList(),
                        exception.Message,
                        now + RetryDelay(pending),
                        cancellationToken);
                }

                return new(
                    exception.Retryable
                        ? CloudSyncRunState.Offline
                        : CloudSyncRunState.Failed,
                    exception.Message,
                    AttemptedAt: now);
            }

            var acknowledgementsByKey = acknowledgements
                .GroupBy(x => x.IdempotencyKey, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.First(),
                    StringComparer.Ordinal);

            foreach (var item in pending)
            {
                if (!acknowledgementsByKey.TryGetValue(
                        item.IdempotencyKey,
                        out var acknowledgement))
                {
                    await _store.MarkRejectedAsync(
                        entitlement.TenantId,
                        item.IdempotencyKey,
                        "missing_ack",
                        "BusinessOS cloud did not acknowledge this synchronization event.",
                        retryable: true,
                        nextAttemptAt: now + RetryDelay(item.AttemptCount + 1),
                        cancellationToken);
                    continue;
                }

                if (string.Equals(
                        acknowledgement.Status,
                        "accepted",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await _store.MarkAcceptedAsync(
                        entitlement.TenantId,
                        item.IdempotencyKey,
                        acknowledgement.ServerId,
                        acknowledgement.ServerUpdatedAt,
                        cancellationToken);
                    pushed++;
                    continue;
                }

                if (!acknowledgement.Retryable)
                    runConflicts++;

                await _store.MarkRejectedAsync(
                    entitlement.TenantId,
                    item.IdempotencyKey,
                    acknowledgement.Code ?? "rejected",
                    acknowledgement.Message ??
                    "BusinessOS cloud rejected this synchronization event.",
                    acknowledgement.Retryable,
                    acknowledgement.Retryable
                        ? now + RetryDelay(item.AttemptCount + 1)
                        : null,
                    cancellationToken);
            }

            // A newly rejected historical sale may only need its stable local
            // references refreshed. Repair it in the same sync cycle so a
            // recoverable reference mismatch does not surface as a conflict.
            await _store.RepairReferenceConflictsAsync(
                entitlement.TenantId,
                user.UserId,
                cancellationToken);
        }

        foreach (var stream in PullStreams)
        {
            var cursor = await _store.GetCursorAsync(
                entitlement.TenantId,
                stream,
                cancellationToken);

            for (var pageNumber = 0;
                 pageNumber < _options.MaxPullPagesPerRun;
                 pageNumber++)
            {
                CloudSyncPullPage page;
                try
                {
                    page = await _transport.PullAsync(
                        accessToken,
                        stream,
                        cursor,
                        _options.PullPageSize,
                        cancellationToken);
                }
                catch (CloudSyncAuthorizationException exception)
                {
                    return new(
                        CloudSyncRunState.LicenseRejected,
                        exception.Message,
                        AttemptedAt: now,
                        Pushed: pushed,
                        Pulled: pulled,
                        Conflicts: runConflicts);
                }
                catch (CloudSyncTransportException exception)
                {
                    return new(
                        exception.Retryable
                            ? CloudSyncRunState.Offline
                            : CloudSyncRunState.Failed,
                        exception.Message,
                        AttemptedAt: now,
                        Pushed: pushed,
                        Pulled: pulled,
                        Conflicts: runConflicts);
                }

                await _store.SaveRemotePageAsync(
                    entitlement.TenantId,
                    stream,
                    page.NextCursor,
                    page.Records,
                    cancellationToken);

                pulled += page.Records.Count;

                if (!page.HasMore ||
                    string.Equals(
                        cursor,
                        page.NextCursor,
                        StringComparison.Ordinal))
                    break;

                cursor = page.NextCursor;
            }
        }

        var queue = await _store.GetQueueSnapshotAsync(
            entitlement.TenantId,
            user.UserId,
            cancellationToken);

        // Queue state is authoritative after same-cycle repair. This prevents a
        // repaired rejection, or another cashier's retained conflict, from
        // leaving the signed-in user stuck in a false conflict state.
        var totalConflicts = queue.Conflicts;

        return new(
            totalConflicts > 0
                ? CloudSyncRunState.Conflicts
                : CloudSyncRunState.Synced,
            totalConflicts > 0
                ? $"{totalConflicts} cloud synchronization conflict(s) require review; local pharmacy data was not changed."
                : "BusinessOS cloud synchronization completed.",
            AttemptedAt: now,
            SucceededAt: _clock.UtcNow,
            Pushed: pushed,
            Pulled: pulled,
            Conflicts: totalConflicts);
    }

    public async Task<CloudSyncConflictReview> GetConflictReviewAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveConflictTenantId();
        if (tenantId is null)
        {
            return CloudSyncConflictReview.Unavailable(
                "Sign in as a pharmacy user to review cloud synchronization conflicts.");
        }

        var conflicts = await _store.GetConflictsAsync(
            tenantId,
            _options.ConflictReviewLimit,
            cancellationToken);

        return new CloudSyncConflictReview(
            conflicts,
            conflicts.Count == 0
                ? "No cloud synchronization conflicts require review."
                : $"{conflicts.Count} cloud synchronization conflict(s) require review. Local pharmacy data was not changed.");
    }

    public async Task<CloudSyncConflictReview> RetryConflictAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveConflictTenantId();
        if (tenantId is null)
        {
            return CloudSyncConflictReview.Unavailable(
                "Sign in as a pharmacy user to resolve cloud synchronization conflicts.");
        }

        var currentUserId = _sessions.Current?.UserId;
        var conflicts = await _store.GetConflictsAsync(
            tenantId,
            _options.ConflictReviewLimit,
            cancellationToken);

        var target = conflicts.FirstOrDefault(
            x => string.Equals(x.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

        if (target is null)
            return new CloudSyncConflictReview(conflicts, "This conflict no longer requires review.");

        if (!string.Equals(target.ActorUserId, currentUserId, StringComparison.Ordinal))
        {
            return new CloudSyncConflictReview(
                conflicts,
                "This conflict belongs to another cashier and can only be retried when they are signed in.");
        }

        await _store.RetryConflictAsync(tenantId, idempotencyKey, cancellationToken);

        return await GetConflictReviewAsync(cancellationToken);
    }

    public async Task<CloudSyncConflictReview> DismissConflictAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveConflictTenantId();
        if (tenantId is null)
        {
            return CloudSyncConflictReview.Unavailable(
                "Sign in as a pharmacy user to resolve cloud synchronization conflicts.");
        }

        await _store.DismissConflictAsync(tenantId, idempotencyKey, cancellationToken);

        return await GetConflictReviewAsync(cancellationToken);
    }

    private string? ResolveConflictTenantId()
    {
        var user = _sessions.Current;
        if (user is null || !user.IsValidAt(_clock.UtcNow))
            return null;

        return user.TenantId;
    }

    private async Task<BusinessOS.Pharmacy.Domain.Licensing.EntitlementSnapshot?>
        GetEntitlementAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _licenses.GetCachedEntitlementAsync(
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan RetryDelay(
        IReadOnlyList<CloudSyncOutboxItem> items)
    {
        var attempt = items.Count == 0
            ? 1
            : items.Max(x => x.AttemptCount) + 1;
        return RetryDelay(attempt);
    }

    private static TimeSpan RetryDelay(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 6);
        var seconds = Math.Min(900, 15 * (1 << exponent));
        return TimeSpan.FromSeconds(seconds);
    }
}
