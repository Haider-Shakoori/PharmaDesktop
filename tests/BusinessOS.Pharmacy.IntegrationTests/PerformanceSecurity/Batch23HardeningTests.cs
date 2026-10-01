using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.LocalClient;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.PerformanceSecurity;

public sealed class Batch23HardeningTests
{
    [Fact]
    public async Task Sqlite_runtime_uses_wal_bounded_cache_and_busy_timeout()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "businessos-pharmacy-b23",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new ApplicationPaths(root);
            await using var provider = new ServiceCollection()
                .AddBusinessOSInfrastructure(paths)
                .AddBusinessOSPersistence()
                .BuildServiceProvider();

            var initializer = provider.GetRequiredService<ILocalDatabaseInitializer>();
            await initializer.InitializeAsync("tenant-b23");

            var factory = provider.GetRequiredService<IDbContextFactory<PharmacyDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            var connection = context.Database.GetDbConnection();

            Assert.Equal("wal", (await ScalarAsync(connection, "PRAGMA journal_mode;"))?.ToString()?.ToLowerInvariant());
            Assert.Equal(10_000L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA busy_timeout;")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA foreign_keys;")));
            Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA temp_store;")));
            Assert.Equal(-8_192L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA cache_size;")));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Pinned_lan_client_does_not_store_terminal_credentials_as_default_headers()
    {
        using var client = PinnedLocalServerTransport.CreatePinnedClient(
            "127.0.0.1",
            5280,
            new string('A', 64));

        Assert.Equal("https", client.BaseAddress?.Scheme);
        Assert.False(client.DefaultRequestHeaders.Contains("X-BusinessOS-Terminal-Id"));
        Assert.False(client.DefaultRequestHeaders.Contains("X-BusinessOS-Terminal-Secret"));
    }

    private static async Task<object?> ScalarAsync(
        System.Data.Common.DbConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
