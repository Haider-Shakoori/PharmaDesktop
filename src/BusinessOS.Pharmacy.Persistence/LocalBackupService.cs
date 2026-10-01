using System.Security.Cryptography;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Backup;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using Microsoft.Data.Sqlite;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalBackupService : ILocalBackupService
{
    private const int ManifestVersion = 1;
    private readonly IApplicationPaths _paths;
    private readonly IClock _clock;
    private readonly IPermissionAuthorizer _permissions;
    private readonly ILocalDatabaseInitializer _initializer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public LocalBackupService(IApplicationPaths paths, IClock clock, IPermissionAuthorizer permissions, ILocalDatabaseInitializer initializer)
    {
        _paths = paths;
        _clock = clock;
        _permissions = permissions;
        _initializer = initializer;
    }

    public async Task<IReadOnlyList<LocalBackupSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        _permissions.Demand("settings.manage");
        _paths.EnsureCreated();
        var rows = new List<LocalBackupSnapshot>();
        foreach (var path in Directory.EnumerateFiles(_paths.BackupsDirectory, "darmaltoon-*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { rows.Add(await DescribeAsync(path, null, true, cancellationToken)); }
            catch { /* A damaged backup remains on disk but is not offered as verified restore input. */ }
        }
        return rows.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<LocalBackupSnapshot> CreateAsync(string expectedTenantId, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("settings.manage");
        ValidateTenant(expectedTenantId);
        await _gate.WaitAsync(cancellationToken);
        try { return await CreateInternalAsync(expectedTenantId, "manual", cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<LocalBackupVerification> VerifyAsync(string backupPath, string expectedTenantId, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("settings.manage");
        ValidateTenant(expectedTenantId);
        try
        {
            var backup = await DescribeAsync(backupPath, expectedTenantId, true, cancellationToken);
            return new(true, "Backup integrity, tenant identity and checksum are valid.", backup);
        }
        catch (Exception exception)
        {
            return new(false, exception.Message, null);
        }
    }

    public async Task<LocalRestoreResult> RestoreAsync(string backupPath, string expectedTenantId, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("settings.manage");
        ValidateTenant(expectedTenantId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var candidate = await DescribeAsync(backupPath, expectedTenantId, true, cancellationToken);
            LocalBackupSnapshot? safety = File.Exists(_paths.DatabasePath)
                ? await CreateInternalAsync(expectedTenantId, "pre-restore", cancellationToken)
                : null;

            var stage = Path.Combine(_paths.RootDirectory, $".restore-stage-{Guid.NewGuid():N}.db");
            var rollback = Path.Combine(_paths.RootDirectory, $".restore-rollback-{Guid.NewGuid():N}.db");
            await CopyFileAsync(candidate.FullPath, stage, cancellationToken);
            var staged = await DescribeAsync(stage, expectedTenantId, false, cancellationToken);
            if (!string.Equals(staged.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The staged restore copy failed checksum verification.");

            var hadOriginal = File.Exists(_paths.DatabasePath);
            var replaced = false;
            try
            {
                if (hadOriginal) await CheckpointAsync(_paths.DatabasePath, cancellationToken);
                SqliteConnection.ClearAllPools();
                DeleteSidecars(_paths.DatabasePath);

                if (hadOriginal)
                    File.Replace(stage, _paths.DatabasePath, rollback, ignoreMetadataErrors: true);
                else
                    File.Move(stage, _paths.DatabasePath);
                replaced = true;

                await _initializer.InitializeAsync(expectedTenantId, cancellationToken);
                var restored = await DescribeAsync(_paths.DatabasePath, expectedTenantId, false, cancellationToken);
                if (File.Exists(rollback)) File.Delete(rollback);
                return new(candidate, safety, _clock.UtcNow);
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                DeleteSidecars(_paths.DatabasePath);
                if (replaced)
                {
                    if (hadOriginal && File.Exists(rollback)) File.Copy(rollback, _paths.DatabasePath, overwrite: true);
                    else if (!hadOriginal && File.Exists(_paths.DatabasePath)) File.Delete(_paths.DatabasePath);
                }
                if (hadOriginal && File.Exists(_paths.DatabasePath))
                    await _initializer.InitializeAsync(expectedTenantId, cancellationToken);
                throw;
            }
            finally
            {
                if (File.Exists(stage)) File.Delete(stage);
                if (File.Exists(rollback)) File.Delete(rollback);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<LocalBackupSnapshot> CreateInternalAsync(string expectedTenantId, string kind, CancellationToken ct)
    {
        if (!File.Exists(_paths.DatabasePath)) throw new InvalidOperationException("The authoritative pharmacy database does not exist yet.");
        var live = await DescribeAsync(_paths.DatabasePath, expectedTenantId, false, ct);
        _paths.EnsureCreated();
        var stamp = _clock.UtcNow.ToUniversalTime().ToString("yyyyMMdd-HHmmssfff");
        var fileName = $"darmaltoon-{kind}-{stamp}-{live.DatabaseInstanceId:N}.db";
        var finalPath = Path.Combine(_paths.BackupsDirectory, fileName);
        var tempPath = finalPath + ".partial";
        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            await source.OpenAsync(ct);
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
            await destination.CloseAsync();
            await source.CloseAsync();

            var snapshot = await DescribeAsync(tempPath, expectedTenantId, false, ct);
            File.Move(tempPath, finalPath, overwrite: false);
            snapshot = snapshot with { FileName = fileName, FullPath = finalPath, CreatedAt = _clock.UtcNow, Kind = kind, IsVerified = true };
            await WriteManifestAsync(snapshot, ct);
            return snapshot;
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    private async Task<LocalBackupSnapshot> DescribeAsync(string path, string? expectedTenantId, bool requireManifestHash, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Backup file was not found.", path);

        string integrity;
        string tenantId;
        Guid databaseInstanceId;
        string? latestMigration;
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA integrity_check;";
                integrity = Convert.ToString(await command.ExecuteScalarAsync(ct)) ?? string.Empty;
            }
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite integrity check failed: {integrity}");

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DatabaseInstanceId, TenantId FROM local_database_identity WHERE Id = 1 LIMIT 1;";
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Backup does not contain a Darmaltoon database identity.");
                if (!Guid.TryParse(reader.GetString(0), out databaseInstanceId)) throw new InvalidOperationException("Backup database identity is invalid.");
                tenantId = reader.GetString(1);
            }
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
                latestMigration = Convert.ToString(await command.ExecuteScalarAsync(ct));
            }
        }

        if (expectedTenantId is not null && !string.Equals(tenantId, expectedTenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("This backup belongs to a different pharmacy tenant. Restore was blocked.");

        var info = new FileInfo(path);
        var sha256 = await HashAsync(path, ct);
        DateTimeOffset createdAt = info.LastWriteTimeUtc;
        var kind = InferKind(info.Name);
        var manifestPath = path + ".manifest.json";
        if (File.Exists(manifestPath))
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, ct)
                ?? throw new InvalidOperationException("Backup manifest is invalid.");
            if (manifest.Version != ManifestVersion || !string.Equals(manifest.TenantId, tenantId, StringComparison.Ordinal) || manifest.DatabaseInstanceId != databaseInstanceId)
                throw new InvalidOperationException("Backup manifest identity does not match the database.");
            if (!string.Equals(manifest.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Backup checksum does not match its manifest. The backup may be damaged or modified.");
            createdAt = manifest.CreatedAt;
            kind = manifest.Kind;
        }
        else if (requireManifestHash)
        {
            throw new InvalidOperationException("Backup manifest is missing. Restore requires a verified Darmaltoon backup.");
        }

        return new(info.Name, path, tenantId, databaseInstanceId, createdAt, info.Length, sha256, latestMigration, kind, true);
    }

    private async Task WriteManifestAsync(LocalBackupSnapshot snapshot, CancellationToken ct)
    {
        var manifest = new BackupManifest(ManifestVersion, snapshot.TenantId, snapshot.DatabaseInstanceId, snapshot.CreatedAt, snapshot.Length, snapshot.Sha256, snapshot.LatestMigration, snapshot.Kind);
        var final = snapshot.FullPath + ".manifest.json";
        var temp = final + ".partial";
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct);
        File.Move(temp, final, overwrite: true);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
    }

    private static async Task CheckpointAsync(string databasePath, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        _ = await command.ExecuteNonQueryAsync(ct);
    }

    private static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string InferKind(string fileName) =>
        fileName.Contains("pre-restore", StringComparison.OrdinalIgnoreCase) ? "pre-restore" : "manual";

    private static void ValidateTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId.Length > 64) throw new ArgumentOutOfRangeException(nameof(tenantId));
    }

    private sealed record BackupManifest(int Version, string TenantId, Guid DatabaseInstanceId, DateTimeOffset CreatedAt, long Length, string Sha256, string? LatestMigration, string Kind);
}
