using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class PosSalesTests
{
    [Fact]
    public async Task Product_search_returns_only_sellable_stock_in_fefo_order()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var medicineId = await CreateMedicineAsync(provider, "POS-SEARCH-1", "Amoxicillin");
            var inventory = provider.GetRequiredService<IInventoryService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "LATE",
                null,
                new DateOnly(2027, 12, 1),
                5m,
                10m,
                25m,
                null));

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "EARLY",
                null,
                new DateOnly(2027, 1, 1),
                3m,
                11m,
                20m,
                null));

            var pos = provider.GetRequiredService<IPosService>();
            var result = await pos.SearchProductsAsync(
                new PosProductSearchFilter("Amoxicillin", location.Id));

            var medicine = Assert.Single(result);
            Assert.Equal(8m, medicine.AvailableQuantity);
            Assert.Equal(20m, medicine.FefoPrice);
            Assert.Equal(20m, medicine.MinPrice);
            Assert.Equal(25m, medicine.MaxPrice);
            Assert.Equal("EARLY", medicine.Batches[0].BatchNumber);
            Assert.Equal("LATE", medicine.Batches[1].BatchNumber);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Checkout_consumes_fefo_calculates_weighted_price_discount_and_cash_change()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var medicineId = await CreateMedicineAsync(provider, "POS-SALE-1", "Cefixime");
            var inventory = provider.GetRequiredService<IInventoryService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "B1",
                null,
                new DateOnly(2027, 1, 1),
                2m,
                8m,
                20m,
                null));

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "B2",
                null,
                new DateOnly(2027, 6, 1),
                5m,
                10m,
                30m,
                null));

            var pos = provider.GetRequiredService<IPosService>();
            var request = new PosCheckoutRequest(
                location.Id,
                null,
                "pos-idempotency-1",
                "Walk-in sale",
                null,
                null,
                null,
                [new PosCheckoutLineRequest(medicineId, 3m, DiscountAmount: 5m)],
                [new PosPaymentRequest("cash", 75m)]);

            var sale = await pos.CheckoutAsync(request);

            Assert.Equal(70m, sale.Subtotal);
            Assert.Equal(5m, sale.DiscountTotal);
            Assert.Equal(65m, sale.Sale.GrandTotal);
            Assert.Equal(65m, sale.Sale.PaidTotal);
            Assert.Equal(0m, sale.Sale.DueTotal);
            Assert.Equal(10m, sale.Sale.ChangeTotal);
            Assert.Equal("paid", sale.Sale.PaymentStatus);

            var syncStore = provider.GetRequiredService<ICloudSyncStore>();
            var pending = await syncStore.GetPendingAsync(
                "tenant-pos",
                "user-pos",
                10,
                DateTimeOffset.UtcNow.AddMinutes(1));
            var outbox = Assert.Single(pending);
            Assert.Equal("sale.completed", outbox.EventType);
            Assert.Equal("pos-idempotency-1", outbox.IdempotencyKey);
            Assert.Equal("tenant-pos", outbox.TenantId);
            Assert.Equal("user-pos", outbox.ActorUserId);
            using (var payload = JsonDocument.Parse(outbox.PayloadJson))
            {
                Assert.Equal(
                    "user-pos",
                    payload.RootElement
                        .GetProperty("cashier_user_id")
                        .GetString());
                Assert.Equal(
                    medicineId,
                    payload.RootElement
                        .GetProperty("lines")[0]
                        .GetProperty("medicine_id")
                        .GetString());
            }

            var line = Assert.Single(sale.Lines);
            Assert.Equal(3m, line.Quantity);
            Assert.Equal(23.3333m, line.UnitPrice);
            Assert.Equal(26m, line.CostTotal);
            Assert.Equal(65m, line.LineTotal);
            Assert.Equal(2, line.Allocations.Count);
            Assert.Equal("B1", line.Allocations[0].BatchNumber);
            Assert.Equal(2m, line.Allocations[0].Quantity);
            Assert.Equal(1m, line.Allocations[1].Quantity);

            var batches = await inventory.SearchBatchesAsync(
                new InventoryBatchFilter(
                    StockLocationId: location.Id,
                    BusinessDate: new DateOnly(2026, 9, 30)));

            Assert.Equal(0m, batches.Single(x => x.BatchNumber == "B1").AvailableQuantity);
            Assert.Equal(4m, batches.Single(x => x.BatchNumber == "B2").AvailableQuantity);

            var replay = await pos.CheckoutAsync(request);
            Assert.Equal(sale.Sale.Id, replay.Sale.Id);

            var afterReplay = await inventory.SearchBatchesAsync(
                new InventoryBatchFilter(
                    StockLocationId: location.Id,
                    BusinessDate: new DateOnly(2026, 9, 30)));

            Assert.Equal(4m, afterReplay.Single(x => x.BatchNumber == "B2").AvailableQuantity);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Credit_sale_requires_customer_respects_credit_limit_and_rolls_back_stock_on_failure()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var medicineId = await CreateMedicineAsync(provider, "POS-CREDIT-1", "Azithromycin");
            var inventory = provider.GetRequiredService<IInventoryService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "CR-1",
                null,
                new DateOnly(2027, 8, 1),
                10m,
                5m,
                100m,
                null));

            var customers = provider.GetRequiredService<ICustomerService>();
            var customerId = await customers.CreateAsync(new SaveCustomerRequest(
                "Credit Customer",
                "0700000000",
                null,
                150m,
                true,
                null));

            var pos = provider.GetRequiredService<IPosService>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    location.Id,
                    customerId,
                    "credit-too-high",
                    null,
                    null,
                    null,
                    null,
                    [new PosCheckoutLineRequest(medicineId, 2m)],
                    [new PosPaymentRequest("credit", 200m)])));

            var afterFailure = await inventory.SearchBatchesAsync(
                new InventoryBatchFilter(StockLocationId: location.Id));
            Assert.Equal(10m, afterFailure.Single(x => x.BatchNumber == "CR-1").AvailableQuantity);

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                location.Id,
                customerId,
                "credit-valid",
                null,
                null,
                null,
                null,
                [new PosCheckoutLineRequest(medicineId, 1m)],
                [new PosPaymentRequest("credit", 100m)]));

            Assert.Equal("credit", sale.Sale.PaymentStatus);
            Assert.Equal(0m, sale.Sale.PaidTotal);
            Assert.Equal(100m, sale.Sale.DueTotal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Subprecision_quantity_is_rejected_without_stock_mutation()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var medicineId = await CreateMedicineAsync(
                provider,
                "POS-TINY-1",
                "Tiny Quantity Medicine");

            var inventory = provider.GetRequiredService<IInventoryService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "TINY-1",
                null,
                new DateOnly(2027, 9, 1),
                1m,
                10m,
                20m,
                null));

            var pos = provider.GetRequiredService<IPosService>();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    location.Id,
                    null,
                    "tiny-quantity",
                    null,
                    null,
                    null,
                    null,
                    [new PosCheckoutLineRequest(medicineId, 0.00001m)],
                    [new PosPaymentRequest("cash", 1m)])));

            var batches = await inventory.SearchBatchesAsync(
                new InventoryBatchFilter(StockLocationId: location.Id));

            Assert.Equal(
                1m,
                batches.Single(x => x.BatchNumber == "TINY-1").AvailableQuantity);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Prescription_only_medicine_requires_reference()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var medicineId = await CreateMedicineAsync(
                provider,
                "RX-1",
                "Prescription Medicine",
                prescriptionRequired: true);

            var inventory = provider.GetRequiredService<IInventoryService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "RX-B",
                null,
                new DateOnly(2027, 5, 1),
                2m,
                20m,
                50m,
                null));

            var pos = provider.GetRequiredService<IPosService>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    location.Id,
                    null,
                    "rx-without-reference",
                    null,
                    null,
                    null,
                    null,
                    [new PosCheckoutLineRequest(medicineId, 1m)],
                    [new PosPaymentRequest("cash", 50m)])));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task<string> CreateMedicineAsync(
        ServiceProvider provider,
        string code,
        string brandName,
        bool prescriptionRequired = false)
    {
        var medicines = provider.GetRequiredService<IMedicineCatalogService>();

        return await medicines.CreateAsync(new SaveMedicineRequest(
            null,
            null,
            code,
            null,
            brandName,
            null,
            "500mg",
            "tablet",
            "pack",
            "tablet",
            10m,
            0m,
            prescriptionRequired,
            true,
            true,
            true,
            null));
    }

    private static async Task InitializeAsync(ServiceProvider provider)
    {
        await provider.GetRequiredService<ILocalDatabaseInitializer>()
            .InitializeAsync("tenant-pos");
    }

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
            "darmaltoon-pos-tests",
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
            "user-pos",
            "tenant-pos",
            "activation-pos",
            "device-pos",
            "POS Tester",
            "pos@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cashier" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pos.sell",
                "pos.discount",
                "pos.price_override",
                "inventory.manage",
                "medicines.manage",
                "customers.manage",
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
