using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Backup;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class BackupRestoreTests
{
    [Fact]
    public async Task Backup_and_restore_round_trip_preserves_authoritative_database_and_creates_safety_backup()
    {
        var root = Temp();
        try
        {
            await using var provider = Build(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync("tenant-backup");
            var db = Path.Combine(root, "pharmacy.db");
            await SetProbeAsync(db, "before-backup");

            var service = provider.GetRequiredService<ILocalBackupService>();
            var backup = await service.CreateAsync("tenant-backup");
            Assert.True(File.Exists(backup.FullPath));
            Assert.True(File.Exists(backup.FullPath + ".manifest.json"));
            Assert.True((await service.VerifyAsync(backup.FullPath, "tenant-backup")).IsValid);

            await SetProbeAsync(db, "after-backup");
            var restored = await service.RestoreAsync(backup.FullPath, "tenant-backup");

            Assert.Equal("before-backup", await GetProbeAsync(db));
            Assert.NotNull(restored.SafetyBackup);
            Assert.True(File.Exists(restored.SafetyBackup!.FullPath));
            Assert.Equal("after-backup", await GetProbeAsync(restored.SafetyBackup.FullPath));
            Assert.True((await service.VerifyAsync(restored.SafetyBackup.FullPath, "tenant-backup")).IsValid);
        }
        finally { SqliteConnection.ClearAllPools(); Delete(root); }
    }

    [Fact]
    public async Task Modified_backup_is_rejected_without_changing_live_database()
    {
        var root = Temp();
        try
        {
            await using var provider = Build(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync("tenant-backup");
            var db = Path.Combine(root, "pharmacy.db");
            await SetProbeAsync(db, "live-value");
            var service = provider.GetRequiredService<ILocalBackupService>();
            var backup = await service.CreateAsync("tenant-backup");

            var corrupt = Path.Combine(root, "backups", "darmaltoon-corrupt.db");
            File.Copy(backup.FullPath, corrupt);
            File.Copy(backup.FullPath + ".manifest.json", corrupt + ".manifest.json");
            await File.AppendAllTextAsync(corrupt, "tamper");

            var verification = await service.VerifyAsync(corrupt, "tenant-backup");
            Assert.False(verification.IsValid);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(corrupt, "tenant-backup"));
            Assert.Equal("live-value", await GetProbeAsync(db));
        }
        finally { SqliteConnection.ClearAllPools(); Delete(root); }
    }

    [Fact]
    public async Task Tenant_mismatch_is_blocked_before_restore()
    {
        var root = Temp();
        try
        {
            await using var provider = Build(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync("tenant-a");
            var db = Path.Combine(root, "pharmacy.db");
            await SetProbeAsync(db, "tenant-a-data");
            var service = provider.GetRequiredService<ILocalBackupService>();
            var backup = await service.CreateAsync("tenant-a");

            Assert.False((await service.VerifyAsync(backup.FullPath, "tenant-b")).IsValid);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(backup.FullPath, "tenant-b"));
            Assert.Equal("tenant-a-data", await GetProbeAsync(db));
        }
        finally { SqliteConnection.ClearAllPools(); Delete(root); }
    }

    private static ServiceProvider Build(string root)
    {
        var services = new ServiceCollection();
        services.AddBusinessOSInfrastructure(new ApplicationPaths(root));
        services.AddSingleton<IPermissionAuthorizer>(new AllowAll());
        services.AddSingleton<IUserSessionService>(new Session());
        services.AddBusinessOSPersistence();
        return services.BuildServiceProvider(true);
    }

    private static async Task SetProbeAsync(string db, string value)
    {
        await using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS backup_probe (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT OR REPLACE INTO backup_probe(id,value) VALUES(1,$v);";
        command.Parameters.AddWithValue("$v", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> GetProbeAsync(string db)
    {
        await using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM backup_probe WHERE id=1;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), "darmaltoon-backup-tests", Guid.NewGuid().ToString("N"));
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class AllowAll : IPermissionAuthorizer { public bool HasPermission(string permission) => true; public void Demand(string permission) { } }
    private sealed class Session : IUserSessionService
    {
        private static readonly UserSessionSnapshot User = new("admin", "tenant-backup", "activation", "device", "Admin", "admin@test.local", new HashSet<string> { "admin" }, new HashSet<string> { "settings.manage" }, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(8));
        public UserSessionSnapshot? Current => User;
        public Task<UserSessionSnapshot> LoginAsync(string email, string password, bool allowOfflineSignIn, CancellationToken cancellationToken = default) => Task.FromResult(User);
        public Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(User);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
