using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class CloudSyncApplyTests
{
    [Fact]
    public async Task Pulled_master_data_is_applied_to_live_local_tables_in_dependency_order()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-sync-apply");

            var sync = provider.GetRequiredService<ICloudSyncStore>();
            var now = DateTimeOffset.UtcNow;

            await sync.SaveRemotePageAsync(
                "tenant-sync-apply",
                "branches",
                "branch-cursor",
                [
                    Remote(
                        "branch-server-1",
                        """
                        {
                          "id":"branch-server-1",
                          "code":"CLOUD",
                          "name":"Cloud Branch",
                          "address":"Kabul",
                          "is_default":false,
                          "is_active":true,
                          "server_created_at":"2026-10-04T06:00:00Z",
                          "server_updated_at":"2026-10-04T06:00:00Z"
                        }
                        """,
                        now),
                ]);

            await sync.SaveRemotePageAsync(
                "tenant-sync-apply",
                "stock_locations",
                "location-cursor",
                [
                    Remote(
                        "location-server-1",
                        """
                        {
                          "id":"location-server-1",
                          "branch_id":"branch-server-1",
                          "code":"CLOUD-MAIN",
                          "name":"Cloud Main Store",
                          "kind":"store",
                          "is_default":false,
                          "is_active":true,
                          "server_created_at":"2026-10-04T06:01:00Z",
                          "server_updated_at":"2026-10-04T06:01:00Z"
                        }
                        """,
                        now),
                ]);

            await sync.SaveRemotePageAsync(
                "tenant-sync-apply",
                "medicines",
                "medicine-cursor",
                [
                    Remote(
                        "medicine-server-1",
                        """
                        {
                          "id":"medicine-server-1",
                          "desktop_source_id":null,
                          "medicine_code":"CLOUD-MED-001",
                          "barcode":"9988776655",
                          "brand_name":"Cloud Medicine",
                          "generic_name":"Cloud Generic",
                          "strength":"500 mg",
                          "dosage_form":"Tablet",
                          "purchase_unit":"box",
                          "sale_unit":"tablet",
                          "units_per_purchase_unit":"100.0000",
                          "reorder_level":"12.0000",
                          "prescription_required":false,
                          "batch_tracking_required":true,
                          "expiry_tracking_required":true,
                          "is_active":true,
                          "notes":"Synced from web",
                          "server_created_at":"2026-10-04T06:02:00Z",
                          "server_updated_at":"2026-10-04T06:02:00Z"
                        }
                        """,
                        now),
                ]);

            await sync.SaveRemotePageAsync(
                "tenant-sync-apply",
                "customers",
                "customer-cursor",
                [
                    Remote(
                        "customer-server-1",
                        """
                        {
                          "id":"customer-server-1",
                          "desktop_source_id":null,
                          "name":"Cloud Customer",
                          "phone":"0700000123",
                          "email":"cloud@example.test",
                          "credit_limit":"500.0000",
                          "is_active":true,
                          "notes":"Cloud-managed customer",
                          "server_created_at":"2026-10-04T06:03:00Z",
                          "server_updated_at":"2026-10-04T06:03:00Z"
                        }
                        """,
                        now),
                ]);

            await sync.SaveRemotePageAsync(
                "tenant-sync-apply",
                "inventory",
                "inventory-cursor",
                [
                    Remote(
                        "batch-server-1",
                        """
                        {
                          "id":"batch-server-1",
                          "medicine_id":"medicine-server-1",
                          "branch_id":"branch-server-1",
                          "stock_location_id":"location-server-1",
                          "batch_number":"CLOUD-LOT-1",
                          "batch_key":"cloud-lot-key-1",
                          "manufactured_at":"2026-01-01",
                          "expires_at":"2027-01-01",
                          "available_quantity":"8.0000",
                          "received_quantity":"10.0000",
                          "sale_price":"25.0000",
                          "purchase_cost":"10.0000",
                          "status":"active",
                          "last_movement_at":"2026-10-04T06:04:00Z",
                          "server_created_at":"2026-10-04T06:04:00Z",
                          "server_updated_at":"2026-10-04T06:04:00Z"
                        }
                        """,
                        now),
                ]);

            var medicines = provider.GetRequiredService<IMedicineCatalogService>();
            var medicineRows = await medicines.SearchAsync(
                new MedicineSearchFilter("CLOUD-MED-001"));
            var medicine = Assert.Single(medicineRows);
            Assert.Equal("Cloud Medicine", medicine.BrandName);
            Assert.Equal(12m, medicine.ReorderLevel);

            var customers = provider.GetRequiredService<ICustomerService>();
            var customerRows = await customers.SearchAsync(
                new CustomerSearchFilter("cloud@example.test"));
            var customer = Assert.Single(customerRows);
            Assert.Equal("Cloud Customer", customer.Name);
            Assert.Equal(500m, customer.CreditLimit);

            var inventory = provider.GetRequiredService<IInventoryService>();
            var batches = await inventory.SearchBatchesAsync(
                new InventoryBatchFilter(Search: "CLOUD-MED-001"));
            var batch = Assert.Single(batches);
            Assert.Equal("CLOUD-LOT-1", batch.BatchNumber);
            Assert.Equal(8m, batch.AvailableQuantity);
            Assert.Equal(10m, batch.PurchaseCost);
            Assert.Equal(25m, batch.SalePrice);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task User_stream_updates_existing_offline_identity_metadata_without_replacing_password_verifier()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-sync-apply");

            var credentials = provider.GetRequiredService<ILocalLanCredentialStore>();
            var validUntil = DateTimeOffset.UtcNow.AddDays(2);

            await credentials.UpsertUserAsync(new CachedLanUser(
                "42",
                "tenant-sync-apply",
                "Old Name",
                "user@example.test",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cashier" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pos.sell" },
                "c2FsdA==",
                "aGFzaA==",
                100000,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                validUntil,
                true));

            var sync = provider.GetRequiredService<ICloudSyncStore>();
            await sync.SaveRemotePageAsync(
                "tenant-sync-apply",
                "users",
                "users-cursor",
                [
                    Remote(
                        "42",
                        """
                        {
                          "id":42,
                          "name":"Updated User",
                          "email":"user@example.test",
                          "is_active":true,
                          "roles":[{"id":5,"name":"Manager","code":"manager"}],
                          "permissions":[
                            {"id":1,"code":"pos.sell","name":"Sell","description":null},
                            {"id":2,"code":"reports.view","name":"Reports","description":null}
                          ],
                          "server_created_at":"2026-10-04T06:00:00Z",
                          "server_updated_at":"2026-10-04T07:00:00Z"
                        }
                        """,
                        DateTimeOffset.UtcNow));

            var updated = await credentials.FindUserByEmailAsync("user@example.test");
            Assert.NotNull(updated);
            Assert.Equal("Updated User", updated!.Name);
            Assert.Contains("manager", updated.Roles);
            Assert.Contains("reports.view", updated.Permissions);
            Assert.Equal("c2FsdA==", updated.PasswordSaltBase64);
            Assert.Equal("aGFzaA==", updated.PasswordHashBase64);
            Assert.Equal(100000, updated.PasswordIterations);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static CloudSyncRemoteRecord Remote(
        string serverId,
        string json,
        DateTimeOffset updatedAt) =>
        new(serverId, json, updatedAt);

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddSingleton<IPermissionAuthorizer>(
            new AllowAllPermissionAuthorizer());
        services.AddSingleton<IUserSessionService>(
            new TestUserSessionService());
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-cloud-sync-apply-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class AllowAllPermissionAuthorizer : IPermissionAuthorizer
    {
        public bool HasPermission(string permission) => true;
        public void Demand(string permission) { }
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private static readonly UserSessionSnapshot Session = new(
            "sync-user",
            "tenant-sync-apply",
            "activation-sync",
            "device-sync",
            "Sync Tester",
            "sync@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "owner" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "medicines.manage",
                "customers.manage",
                "inventory.manage",
                "inventory.status",
            },
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(8));

        public UserSessionSnapshot? Current => Session;

        public Task<UserSessionSnapshot> LoginAsync(
            string email,
            string password,
            bool allowOfflineSignIn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task<UserSessionSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task LogoutAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
