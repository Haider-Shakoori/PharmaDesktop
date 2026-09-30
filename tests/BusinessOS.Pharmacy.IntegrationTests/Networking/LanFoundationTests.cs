using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.LocalClient;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Networking;

public sealed class LanFoundationTests
{
    [Fact]
    public async Task Network_configuration_round_trips_non_secret_client_identity()
    {
        var root = CreateTemporaryRoot();

        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureCreated();
            var store = new NetworkConfigurationStore(paths);

            var configuration = new NetworkConfiguration
            {
                Mode = DeploymentMode.Client,
                ServerHost = "PHARMACY-SERVER",
                ServerPort = 5280,
                ServerId = "0199c7ef-45d3-7e31-8be3-111111111111",
                ServerCertificateSha256 = new string('A', 64),
                TenantId = "tenant-1",
                TerminalId = "0199c7ef-45d3-7e31-8be3-222222222222",
                TerminalName = "Front Counter 1",
                TerminalRole = "POS Terminal",
                IsConfigured = true,
            };

            await store.SaveAsync(configuration);
            var loaded = await store.LoadAsync();

            Assert.Equal(DeploymentMode.Client, loaded.Mode);
            Assert.Equal("PHARMACY-SERVER", loaded.ServerHost);
            Assert.Equal(configuration.ServerId, loaded.ServerId);
            Assert.Equal(configuration.TerminalId, loaded.TerminalId);

            var json = await File.ReadAllTextAsync(paths.NetworkConfigurationPath);
            Assert.DoesNotContain("terminal-secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("license_key", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Server_identity_is_persistent_and_tenant_bound()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildServerProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var terminals = provider.GetRequiredService<ILocalTerminalService>();

            var first = await terminals.GetOrCreateServerIdentityAsync(
                "tenant-a",
                "Main Pharmacy Server");
            var second = await terminals.GetOrCreateServerIdentityAsync(
                "tenant-a",
                "Renamed Pharmacy Server");

            Assert.Equal(first.ServerId, second.ServerId);
            Assert.Equal("tenant-a", second.TenantId);
            Assert.Equal("Renamed Pharmacy Server", second.ServerName);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                terminals.GetOrCreateServerIdentityAsync(
                    "tenant-b",
                    "Wrong Tenant Server"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Pairing_code_is_one_time_and_terminal_secret_is_required()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildServerProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync(
                "tenant-a",
                "Main Pharmacy Server");

            var pairing = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            var terminalId = Guid.CreateVersion7().ToString();

            var paired = await terminals.PairAsync(
                new PairTerminalRequest(
                    pairing.Code,
                    terminalId,
                    "Front Counter 1",
                    "CASHIER-PC-01",
                    "POS Terminal"));

            Assert.Equal(terminalId, paired.TerminalId);
            Assert.Equal("tenant-a", paired.TenantId);
            Assert.False(string.IsNullOrWhiteSpace(paired.TerminalSecret));

            var authenticated = await terminals.AuthenticateTerminalAsync(
                paired.TerminalId,
                paired.TerminalSecret);
            Assert.NotNull(authenticated);
            Assert.Equal("Front Counter 1", authenticated!.Name);

            Assert.Null(await terminals.AuthenticateTerminalAsync(
                paired.TerminalId,
                "wrong-terminal-secret"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                terminals.PairAsync(
                    new PairTerminalRequest(
                        pairing.Code,
                        Guid.CreateVersion7().ToString(),
                        "Replay",
                        "REPLAY-PC",
                        "POS Terminal")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Revoked_terminal_cannot_authenticate()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildServerProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync(
                "tenant-a",
                "Main Pharmacy Server");

            var pairing = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            var paired = await terminals.PairAsync(
                new PairTerminalRequest(
                    pairing.Code,
                    Guid.CreateVersion7().ToString(),
                    "Manager Office",
                    "MANAGER-PC",
                    "Manager Terminal"));

            await terminals.RevokeAsync(paired.TerminalId);

            Assert.Null(await terminals.AuthenticateTerminalAsync(
                paired.TerminalId,
                paired.TerminalSecret));

            var listing = await terminals.ListAsync();
            var revoked = Assert.Single(listing);
            Assert.False(revoked.IsActive);
            Assert.NotNull(revoked.RevokedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Expired_pairing_code_is_rejected()
    {
        var root = CreateTemporaryRoot();
        var clock = new TestClock(
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));

        try
        {
            await using var provider = BuildServerProvider(root, clock);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync(
                "tenant-a",
                "Main Pharmacy Server");

            var pairing = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(1));
            clock.UtcNow = clock.UtcNow.AddMinutes(2);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                terminals.PairAsync(
                    new PairTerminalRequest(
                        pairing.Code,
                        Guid.CreateVersion7().ToString(),
                        "Late Terminal",
                        "LATE-PC",
                        "POS Terminal")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Client_service_graph_does_not_register_authoritative_database_services()
    {
        var root = CreateTemporaryRoot();

        try
        {
            var services = new ServiceCollection();
            var paths = new ApplicationPaths(root);

            services.AddBusinessOSInfrastructure(paths);
            services.AddBusinessOSLocalClient();

            using var provider = services.BuildServiceProvider();

            Assert.Null(provider.GetService<ILocalDatabaseInitializer>());
            Assert.NotNull(provider.GetRequiredService<BusinessOS.Pharmacy.Application.Abstractions.Authentication.IUserSessionService>());
            Assert.NotNull(provider.GetRequiredService<BusinessOS.Pharmacy.Application.Abstractions.Medicines.IMedicineCatalogService>());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Lan_user_session_is_bound_to_terminal_and_rejects_tampering()
    {
        var root = CreateTemporaryRoot();
        var clock = new TestClock(
            new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero));

        try
        {
            await using var provider = BuildServerProvider(root, clock);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var credentials = provider.GetRequiredService<ILocalLanCredentialStore>();
            var terminalId = Guid.CreateVersion7().ToString();
            var otherTerminalId = Guid.CreateVersion7().ToString();

            var principal = new LocalLanSessionPrincipal(
                string.Empty,
                terminalId,
                "user-1",
                "tenant-a",
                "Cashier A",
                "cashier@example.test",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cashier" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pos.sell" },
                clock.UtcNow,
                clock.UtcNow.AddHours(1),
                null);

            var created = await credentials.CreateSessionAsync(
                principal,
                TimeSpan.FromHours(1));

            var authenticated = await credentials.AuthenticateSessionAsync(
                terminalId,
                created.SessionToken);

            Assert.NotNull(authenticated);
            Assert.Equal("user-1", authenticated!.UserId);
            Assert.True(authenticated.HasPermission("pos.sell"));

            Assert.Null(await credentials.AuthenticateSessionAsync(
                otherTerminalId,
                created.SessionToken));

            Assert.Null(await credentials.AuthenticateSessionAsync(
                terminalId,
                created.SessionToken + "tampered"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Lan_user_session_expires_and_terminal_session_revocation_is_effective()
    {
        var root = CreateTemporaryRoot();
        var clock = new TestClock(
            new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero));

        try
        {
            await using var provider = BuildServerProvider(root, clock);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-a");

            var credentials = provider.GetRequiredService<ILocalLanCredentialStore>();
            var terminalId = Guid.CreateVersion7().ToString();

            LocalLanSessionPrincipal NewPrincipal() => new(
                string.Empty,
                terminalId,
                "user-2",
                "tenant-a",
                "Cashier B",
                "cashier-b@example.test",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cashier" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pos.sell" },
                clock.UtcNow,
                clock.UtcNow.AddMinutes(5),
                null);

            var first = await credentials.CreateSessionAsync(
                NewPrincipal(),
                TimeSpan.FromMinutes(5));

            clock.UtcNow = clock.UtcNow.AddMinutes(6);

            Assert.Null(await credentials.AuthenticateSessionAsync(
                terminalId,
                first.SessionToken));

            var second = await credentials.CreateSessionAsync(
                NewPrincipal(),
                TimeSpan.FromMinutes(5));

            Assert.NotNull(await credentials.AuthenticateSessionAsync(
                terminalId,
                second.SessionToken));

            await credentials.RevokeSessionsForTerminalAsync(terminalId);

            Assert.Null(await credentials.AuthenticateSessionAsync(
                terminalId,
                second.SessionToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Client_configuration_rejects_missing_server_identity_or_certificate()
    {
        var missingIdentity = new NetworkConfiguration
        {
            Mode = DeploymentMode.Client,
            ServerHost = "PHARMACY-SERVER",
            ServerPort = 5280,
            TenantId = "tenant-a",
            TerminalId = Guid.CreateVersion7().ToString(),
            IsConfigured = true,
        };

        Assert.Throws<InvalidOperationException>(missingIdentity.Validate);

        var invalidPort = new NetworkConfiguration
        {
            Mode = DeploymentMode.Server,
            ServerPort = 80,
            IsConfigured = true,
        };

        Assert.Throws<InvalidOperationException>(invalidPort.Validate);
    }

    private static ServiceProvider BuildServerProvider(
        string root,
        TestClock? clock = null)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);

        if (clock is not null)
        {
            services.AddSingleton<IClock>(clock);
        }

        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider();
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-lan-tests",
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
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
