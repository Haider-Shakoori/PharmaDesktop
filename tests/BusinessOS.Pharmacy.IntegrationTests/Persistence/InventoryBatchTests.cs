using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class InventoryBatchTests
{
    [Fact]
    public async Task Inventory_defaults_and_opening_stock_create_audited_batch()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var inventory = provider.GetRequiredService<IInventoryService>();
            var catalog = provider.GetRequiredService<IMedicineCatalogService>();

            await inventory.EnsureDefaultsAsync();
            await inventory.EnsureDefaultsAsync();

            var references = await inventory.GetReferenceDataAsync();
            var branch = Assert.Single(references.Branches);
            var location = Assert.Single(references.Locations);

            Assert.Equal("MAIN", branch.Code);
            Assert.True(branch.IsDefault);
            Assert.Equal("MAIN", location.Code);
            Assert.True(location.IsDefault);

            var medicineId = await CreateMedicineAsync(catalog, "INV-001", "Inventory Medicine");
            var batchId = await inventory.CreateOpeningStockAsync(
                new CreateOpeningStockRequest(
                    medicineId,
                    location.Id,
                    "LOT-001",
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2027, 9, 1),
                    10m,
                    50m,
                    75m,
                    "Opening count"));

            var detail = await inventory.GetBatchAsync(batchId);
            Assert.NotNull(detail);
            Assert.Equal(10m, detail!.Batch.ReceivedQuantity);
            Assert.Equal(10m, detail.Batch.AvailableQuantity);
            Assert.Equal(50m, detail.Batch.PurchaseCost);
            Assert.Equal(75m, detail.Batch.SalePrice);
            Assert.Equal("active", detail.Batch.Status);

            var movement = Assert.Single(detail.Movements);
            Assert.Equal("opening_stock", movement.MovementType);
            Assert.Equal(10m, movement.QuantityDelta);
            Assert.Equal(10m, movement.BalanceAfter);
            Assert.Equal("Opening count", movement.Reason);

            var sameBatchId = await inventory.CreateOpeningStockAsync(
                new CreateOpeningStockRequest(
                    medicineId,
                    location.Id,
                    "LOT-001",
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2027, 9, 1),
                    10m,
                    70m,
                    80m,
                    "Second opening count"));

            Assert.Equal(batchId, sameBatchId);

            var updated = await inventory.GetBatchAsync(batchId);
            Assert.NotNull(updated);
            Assert.Equal(20m, updated!.Batch.ReceivedQuantity);
            Assert.Equal(20m, updated.Batch.AvailableQuantity);
            Assert.Equal(60m, updated.Batch.PurchaseCost);
            Assert.Equal(80m, updated.Batch.SalePrice);
            Assert.Equal(2, updated.Movements.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Negative_adjustment_rolls_back_without_partial_inventory_state()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var inventory = provider.GetRequiredService<IInventoryService>();
            var catalog = provider.GetRequiredService<IMedicineCatalogService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            var medicineId = await CreateMedicineAsync(catalog, "INV-GUARD", "Guard Medicine");
            var batchId = await inventory.CreateOpeningStockAsync(
                new CreateOpeningStockRequest(
                    medicineId,
                    location.Id,
                    "GUARD-1",
                    null,
                    new DateOnly(2027, 1, 1),
                    5m,
                    10m,
                    15m,
                    null));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                inventory.AdjustAsync(
                    new InventoryAdjustmentRequest(
                        batchId,
                        -6m,
                        "count_correction",
                        "Impossible negative count")));

            var detail = await inventory.GetBatchAsync(batchId);
            Assert.NotNull(detail);
            Assert.Equal(5m, detail!.Batch.AvailableQuantity);
            Assert.Single(detail.Movements);
            Assert.DoesNotContain(detail.Movements, x => x.MovementType == "adjustment");

            var paths = provider.GetRequiredService<ApplicationPaths>();
            await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
            await connection.OpenAsync();

            await using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM inventory_adjustments;";
            Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Adjustment_and_batch_status_changes_are_audited()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var inventory = provider.GetRequiredService<IInventoryService>();
            var catalog = provider.GetRequiredService<IMedicineCatalogService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            var medicineId = await CreateMedicineAsync(catalog, "INV-AUDIT", "Audit Medicine");
            var batchId = await inventory.CreateOpeningStockAsync(
                new CreateOpeningStockRequest(
                    medicineId,
                    location.Id,
                    "AUDIT-1",
                    null,
                    new DateOnly(2027, 6, 1),
                    10m,
                    20m,
                    30m,
                    null));

            var adjustment = await inventory.AdjustAsync(
                new InventoryAdjustmentRequest(
                    batchId,
                    -2m,
                    "damage",
                    "Two damaged units"));

            Assert.StartsWith("ADJ-", adjustment.Number, StringComparison.Ordinal);
            Assert.Equal(8m, adjustment.BalanceAfter);

            await inventory.ChangeBatchStatusAsync(
                new ChangeBatchStatusRequest(
                    batchId,
                    "quarantined",
                    "Quality review"));

            var detail = await inventory.GetBatchAsync(batchId);
            Assert.NotNull(detail);
            Assert.Equal(8m, detail!.Batch.AvailableQuantity);
            Assert.Equal("quarantined", detail.Batch.Status);
            Assert.Contains(detail.Movements, movement =>
                movement.MovementType == "adjustment" &&
                movement.QuantityDelta == -2m &&
                movement.BalanceAfter == 8m);

            var status = Assert.Single(detail.StatusEvents);
            Assert.Equal("active", status.FromStatus);
            Assert.Equal("quarantined", status.ToStatus);
            Assert.Equal("Quality review", status.Reason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Fefo_skips_expired_and_non_active_batches_and_is_idempotent()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var inventory = provider.GetRequiredService<IInventoryService>();
            var allocation = provider.GetRequiredService<IStockAllocationService>();
            var catalog = provider.GetRequiredService<IMedicineCatalogService>();

            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();
            var medicineId = await CreateMedicineAsync(catalog, "INV-FEFO", "FEFO Medicine");

            var expired = await OpenAsync(inventory, medicineId, location.Id, "EXPIRED", new DateOnly(2026, 9, 29), 5m);
            var quarantined = await OpenAsync(inventory, medicineId, location.Id, "QUAR", new DateOnly(2026, 10, 10), 5m);
            var early = await OpenAsync(inventory, medicineId, location.Id, "EARLY", new DateOnly(2026, 10, 20), 5m);
            var late = await OpenAsync(inventory, medicineId, location.Id, "LATE", new DateOnly(2026, 12, 1), 5m);

            await inventory.ChangeBatchStatusAsync(
                new ChangeBatchStatusRequest(
                    quarantined,
                    "quarantined",
                    "Hold for review"));

            var request = new StockAllocationRequest(
                medicineId,
                location.Id,
                6m,
                "test_sale",
                "SALE-001",
                "sale-001",
                "FEFO regression",
                RequirePriced: true);

            var first = await allocation.ConsumeFefoAsync(request);
            Assert.Equal(2, first.Count);
            Assert.Equal(early, first[0].ProductBatchId);
            Assert.Equal(5m, first[0].Quantity);
            Assert.Equal(late, first[1].ProductBatchId);
            Assert.Equal(1m, first[1].Quantity);

            var replay = await allocation.ConsumeFefoAsync(request);
            Assert.Equal(
                first.Select(x => x.StockMovementId),
                replay.Select(x => x.StockMovementId));

            Assert.Equal(5m, (await inventory.GetBatchAsync(expired))!.Batch.AvailableQuantity);
            Assert.Equal(5m, (await inventory.GetBatchAsync(quarantined))!.Batch.AvailableQuantity);
            Assert.Equal(0m, (await inventory.GetBatchAsync(early))!.Batch.AvailableQuantity);
            Assert.Equal("depleted", (await inventory.GetBatchAsync(early))!.Batch.Status);
            Assert.Equal(4m, (await inventory.GetBatchAsync(late))!.Batch.AvailableQuantity);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Two_simultaneous_allocations_cannot_drive_stock_negative()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var inventory = provider.GetRequiredService<IInventoryService>();
            var allocation = provider.GetRequiredService<IStockAllocationService>();
            var catalog = provider.GetRequiredService<IMedicineCatalogService>();

            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();
            var medicineId = await CreateMedicineAsync(catalog, "INV-CONC", "Concurrent Medicine");
            var batchId = await OpenAsync(
                inventory,
                medicineId,
                location.Id,
                "CONC-1",
                new DateOnly(2027, 1, 1),
                5m);

            async Task<bool> AttemptAsync(string saleId)
            {
                try
                {
                    await allocation.ConsumeFefoAsync(
                        new StockAllocationRequest(
                            medicineId,
                            location.Id,
                            4m,
                            "test_sale",
                            saleId,
                            saleId.ToLowerInvariant(),
                            RequirePriced: true));
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            var outcomes = await Task.WhenAll(
                AttemptAsync("SALE-A"),
                AttemptAsync("SALE-B"));

            Assert.Equal(1, outcomes.Count(success => success));
            Assert.Equal(1, outcomes.Count(success => !success));

            var detail = await inventory.GetBatchAsync(batchId);
            Assert.NotNull(detail);
            Assert.Equal(1m, detail!.Batch.AvailableQuantity);
            Assert.DoesNotContain(detail.Movements, movement => movement.BalanceAfter < 0);
            Assert.Single(detail.Movements, x => x.MovementType == "sale");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task InitializeAsync(ServiceProvider provider)
    {
        await provider.GetRequiredService<ILocalDatabaseInitializer>()
            .InitializeAsync("tenant-inventory");
    }

    private static async Task<string> CreateMedicineAsync(
        IMedicineCatalogService catalog,
        string code,
        string name) =>
        await catalog.CreateAsync(
            new SaveMedicineRequest(
                null,
                null,
                code,
                null,
                name,
                name,
                "500 mg",
                "Tablet",
                "box",
                "tablet",
                100m,
                10m,
                false,
                true,
                true,
                true,
                null));

    private static Task<string> OpenAsync(
        IInventoryService inventory,
        string medicineId,
        string locationId,
        string batch,
        DateOnly expiresAt,
        decimal quantity) =>
        inventory.CreateOpeningStockAsync(
            new CreateOpeningStockRequest(
                medicineId,
                locationId,
                batch,
                null,
                expiresAt,
                quantity,
                10m,
                20m,
                null));

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddSingleton<IPermissionAuthorizer>(new AllowAllPermissionAuthorizer());
        services.AddSingleton<IUserSessionService>(new TestUserSessionService());
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-inventory-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AllowAllPermissionAuthorizer : IPermissionAuthorizer
    {
        public bool HasPermission(string permission) => true;

        public void Demand(string permission)
        {
        }
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private static readonly UserSessionSnapshot Session = new(
            "user-inventory",
            "tenant-inventory",
            "activation-inventory",
            "device-inventory",
            "Inventory Tester",
            "inventory@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inventory" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "medicines.manage",
                "inventory.manage",
                "inventory.adjust",
                "inventory.status",
                "pos.sell",
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

        public Task LogoutAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
