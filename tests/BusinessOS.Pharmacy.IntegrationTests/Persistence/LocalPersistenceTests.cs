using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class LocalPersistenceTests
{
    [Fact]
    public async Task Initialize_applies_migrations_enables_wal_and_binds_database_to_tenant()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            var paths = provider.GetRequiredService<ApplicationPaths>();
            var initializer = provider.GetRequiredService<ILocalDatabaseInitializer>();

            var identity = await initializer.InitializeAsync("tenant-a");

            Assert.Equal("tenant-a", identity.TenantId);
            Assert.NotEqual(Guid.Empty, identity.DatabaseInstanceId);
            Assert.True(File.Exists(paths.DatabasePath));

            await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
            await connection.OpenAsync();

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            Assert.Contains("__EFMigrationsHistory", tables);
            Assert.Contains("local_database_identity", tables);
            Assert.Contains("local_settings", tables);
            Assert.Contains("local_sequences", tables);

            await using var journalMode = connection.CreateCommand();
            journalMode.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", Convert.ToString(await journalMode.ExecuteScalarAsync())?.ToLowerInvariant());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Initialize_rejects_a_different_tenant_without_rebinding_the_database()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            var initializer = provider.GetRequiredService<ILocalDatabaseInitializer>();

            await initializer.InitializeAsync("tenant-a");

            var error = await Assert.ThrowsAsync<LocalDatabaseTenantMismatchException>(
                () => initializer.InitializeAsync("tenant-b"));

            Assert.Equal("tenant-a", error.ExpectedTenantId);
            Assert.Equal("tenant-b", error.ActualTenantId);

            var original = await initializer.InitializeAsync("tenant-a");
            Assert.Equal("tenant-a", original.TenantId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Local_settings_round_trip_non_secret_values_and_reject_secret_keys()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var settings = provider.GetRequiredService<ILocalSettingsStore>();

            await settings.SetAsync("ui.language", "fa");
            Assert.Equal("fa", await settings.GetAsync("ui.language"));

            await settings.RemoveAsync("ui.language");
            Assert.Null(await settings.GetAsync("ui.language"));

            await Assert.ThrowsAsync<ArgumentException>(
                () => settings.SetAsync("auth.access_token", "must-not-be-stored"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Cloud_sync_conflict_review_orders_DateTimeOffset_values_in_memory_for_sqlite()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            var paths = provider.GetRequiredService<ApplicationPaths>();
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
            {
                await connection.OpenAsync();

                foreach (var item in new[]
                {
                    (Id: "conflict-old", Key: "sale:old", CreatedAt: new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)),
                    (Id: "conflict-new", Key: "sale:new", CreatedAt: new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero)),
                })
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        INSERT INTO cloud_sync_outbox
                        (id, tenant_id, actor_user_id, event_type, idempotency_key, payload_json,
                         status, attempt_count, last_error_code, last_error_message, created_at, updated_at)
                        VALUES
                        (@id, 'tenant-a', 'user-a', 'sale.completed', @key, @payload,
                         'conflict', 1, 'reference_missing', 'Missing reference', @created, @updated);
                        """;
                    command.Parameters.AddWithValue("@id", item.Id);
                    command.Parameters.AddWithValue("@key", item.Key);
                    command.Parameters.AddWithValue(
                        "@payload",
                        $"{{\"local_id\":\"{item.Id}\",\"business_date\":\"2026-10-03\"}}");
                    command.Parameters.AddWithValue("@created", item.CreatedAt);
                    command.Parameters.AddWithValue("@updated", item.CreatedAt);
                    await command.ExecuteNonQueryAsync();
                }
            }

            var store = provider.GetRequiredService<ICloudSyncStore>();
            var conflicts = await store.GetConflictsAsync("tenant-a", 10);

            Assert.Equal(2, conflicts.Count);
            Assert.Equal("sale:new", conflicts[0].IdempotencyKey);
            Assert.Equal("sale:old", conflicts[1].IdempotencyKey);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Local_sequence_allocation_is_atomic_across_concurrent_writers()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var sequence = provider.GetRequiredService<ILocalSequenceService>();

            var allocated = await Task.WhenAll(
                Enumerable.Range(0, 20)
                    .Select(_ => sequence.NextAsync("sales")));

            Assert.Equal(
                Enumerable.Range(1, 20).Select(value => (long)value),
                allocated.OrderBy(value => value));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "businessos-pharmacy-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
