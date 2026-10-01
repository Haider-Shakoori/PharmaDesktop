using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Domain.Licensing;
using BusinessOS.Pharmacy.Licensing;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Licensing;

public sealed class ProtectedWindowsStateStoreTests
{
    [Fact]
    public async Task Activation_store_round_trips_entitlement_feature_set_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TestPaths();
        var store = new WindowsActivationStore(fixture);

        var state = new ActivationState(
            "lease-token",
            "device-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new EntitlementSnapshot(
                "tenant-1",
                "subscription-1",
                "license-1",
                1,
                "activation-1",
                "device-1",
                "TRIAL",
                SubscriptionState.Trial,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(7),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "advanced_reports",
                    "desktop_sync",
                }));

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("tenant-1", loaded!.Entitlement.TenantId);
        Assert.True(loaded.Entitlement.Features.Contains("ADVANCED_REPORTS"));
        Assert.True(loaded.Entitlement.Features.Contains("desktop_sync"));
    }

    [Fact]
    public async Task User_session_store_round_trips_role_and_permission_sets_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TestPaths();
        var store = new WindowsUserSessionStore(fixture);
        var now = DateTimeOffset.UtcNow;

        var state = new DesktopSessionState(
            "access-token",
            new UserSessionSnapshot(
                "user-1",
                "tenant-1",
                "activation-1",
                "device-1",
                "Owner",
                "owner@example.test",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "owner" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pos.sell", "users.manage" },
                now,
                now.AddHours(1)),
            now,
            now,
            null);

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.True(loaded!.User.Roles.Contains("OWNER"));
        Assert.True(loaded.User.Permissions.Contains("POS.SELL"));
        Assert.True(loaded.User.Permissions.Contains("users.manage"));
    }

    private sealed class TestPaths : IApplicationPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                "DarmaltoonTests",
                Guid.NewGuid().ToString("N"));

            DatabasePath = Path.Combine(RootDirectory, "pharmacy.db");
            BackupsDirectory = Path.Combine(RootDirectory, "backups");
            UpdatesDirectory = Path.Combine(RootDirectory, "updates");
            LogsDirectory = Path.Combine(RootDirectory, "logs");
            LicensingDirectory = Path.Combine(RootDirectory, "licensing");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            CertificatesDirectory = Path.Combine(RootDirectory, "certificates");
            NetworkConfigurationPath = Path.Combine(ConfigDirectory, "network.json");
            NetworkSecretsPath = Path.Combine(ConfigDirectory, "network-secrets.bin");
            ServerCertificatePath = Path.Combine(CertificatesDirectory, "local-server.pfx");
            NetworkLogPath = Path.Combine(LogsDirectory, "network.log");
            InstallationIdPath = Path.Combine(LicensingDirectory, "installation.id");
            ActivationStatePath = Path.Combine(LicensingDirectory, "activation.bin");
            UserSessionStatePath = Path.Combine(LicensingDirectory, "user-session.bin");

            EnsureCreated();
        }

        public string RootDirectory { get; }
        public string DatabasePath { get; }
        public string BackupsDirectory { get; }
        public string UpdatesDirectory { get; }
        public string LogsDirectory { get; }
        public string LicensingDirectory { get; }
        public string ConfigDirectory { get; }
        public string CertificatesDirectory { get; }
        public string NetworkConfigurationPath { get; }
        public string NetworkSecretsPath { get; }
        public string ServerCertificatePath { get; }
        public string NetworkLogPath { get; }
        public string InstallationIdPath { get; }
        public string ActivationStatePath { get; }
        public string UserSessionStatePath { get; }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(UpdatesDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(LicensingDirectory);
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(CertificatesDirectory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; Windows DPAPI files are test-only.
            }
        }
    }
}
