using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class SyncPersistenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 30, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Queue_is_durable_and_idempotency_key_cannot_be_reused_for_different_data()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-sync");

            var queue = provider.GetRequiredService<ISyncQueueStore>();
            var request = Request("sale-1", "sync-sale-1");

            var first = await queue.EnqueueAsync(request);
            var duplicate = await queue.EnqueueAsync(request);

            Assert.Equal(first, duplicate);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(request with
                {
                    PayloadJson = "{\"id\":\"sale-1\",\"total\":999}",
                }));

            var claimed = await queue.ClaimPendingAsync(10, Now);

            var item = Assert.Single(claimed);
            Assert.Equal(first, item.Id);
            Assert.Equal(1, item.AttemptCount);
            Assert.Equal(SyncQueueState.Syncing, item.State);

            await queue.MarkSyncedAsync(
                item.Id,
                "cloud-sale-1",
                "v1",
                Now.AddSeconds(1));

            var status = await queue.GetStatusAsync();
            Assert.Equal(0, status.Pending);
            Assert.Equal(0, status.Syncing);
            Assert.Equal(Now.AddSeconds(1), status.LastSuccessfulSyncAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Retry_due_time_and_stale_claim_recovery_are_restart_safe()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-sync");

            var queue = provider.GetRequiredService<ISyncQueueStore>();
            await queue.EnqueueAsync(Request("sale-2", "sync-sale-2"));

            var first = Assert.Single(await queue.ClaimPendingAsync(10, Now));

            await queue.MarkRetryAsync(
                first.Id,
                "temporary network failure",
                Now.AddMinutes(1));

            Assert.Empty(await queue.ClaimPendingAsync(10, Now.AddSeconds(30)));

            var second = Assert.Single(
                await queue.ClaimPendingAsync(10, Now.AddMinutes(1)));

            Assert.Equal(2, second.AttemptCount);

            var recovered = Assert.Single(
                await queue.ClaimPendingAsync(10, Now.AddMinutes(7)));

            Assert.Equal(second.Id, recovered.Id);
            Assert.Equal(3, recovered.AttemptCount);
            Assert.Equal(SyncQueueState.Syncing, recovered.State);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Checkpoints_and_conflicts_survive_in_local_database()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-sync");

            var queue = provider.GetRequiredService<ISyncQueueStore>();
            await queue.SetCheckpointAsync("medicines", "cursor-10", Now);

            var checkpoints = await queue.GetCheckpointsAsync();
            Assert.Equal("cursor-10", checkpoints["medicines"]);

            var queueId = await queue.EnqueueAsync(
                Request("closing-1", "sync-closing-1") with
                {
                    Stream = "daily_closings",
                });

            _ = await queue.ClaimPendingAsync(10, Now);

            await queue.MarkConflictAsync(
                queueId,
                new SyncConflictRecord(
                    "daily_closings",
                    "closing-1",
                    "sync-closing-1",
                    SyncConsistencyClass.ImmutableTransaction,
                    "{\"total\":100}",
                    "{\"total\":200}",
                    "Immutable daily closing conflict.",
                    Now));

            await queue.RecordPullConflictAsync(
                new SyncConflictRecord(
                    "medicines",
                    "medicine-1",
                    "cloud-medicine-1-v2",
                    SyncConsistencyClass.VersionedMasterData,
                    "{\"brand_name\":\"Local\"}",
                    "{\"brand_name\":\"Remote\"}",
                    "Both sides changed the medicine.",
                    Now));

            var status = await queue.GetStatusAsync();
            Assert.Equal(1, status.Conflicts);

            var paths = provider.GetRequiredService<ApplicationPaths>();
            await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sync_conflicts;";
            Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static SyncEnqueueRequest Request(
        string entityId,
        string idempotencyKey) =>
        new(
            "sales",
            entityId,
            SyncOperation.Append,
            SyncConsistencyClass.ImmutableTransaction,
            $"{{\"id\":\"{entityId}\",\"total\":100}}",
            idempotencyKey,
            1,
            Now);

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddSingleton<IClock>(new TestClock(Now));
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-sync-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
