using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class SaleReturnTests
{
    [Fact]
    public async Task Return_restock_is_allocation_aware_and_idempotent()
    {
        var root = CreateTemporaryRoot();
        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);
            var setup = await CreateSaleAsync(provider, 3m, 20m, 0m);
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var inventory = provider.GetRequiredService<IInventoryService>();
            var line = Assert.Single((await returns.GetReturnableSaleAsync(setup.Sale.Sale.Id))!.Lines);
            var request = new ProcessSaleReturnRequest(setup.Sale.Sale.Id, "return-idempotency-1", "Customer changed medicine",
                [new ProcessSaleReturnLineRequest(line.Id, 2m)], [new ProcessSaleReturnRefundRequest("cash", 40m)]);
            var result = await returns.ProcessAsync(request);
            Assert.Equal(40m, result.Return.RefundTotal);
            Assert.All(result.Lines.SelectMany(x => x.Allocations), x => Assert.True(x.Restocked));
            var batches = await inventory.SearchBatchesAsync(new InventoryBatchFilter(StockLocationId: setup.LocationId));
            Assert.Equal(4m, batches.Single().AvailableQuantity);
            var replay = await returns.ProcessAsync(request);
            Assert.Equal(result.Return.Id, replay.Return.Id);
            var afterReplay = await inventory.SearchBatchesAsync(new InventoryBatchFilter(StockLocationId: setup.LocationId));
            Assert.Equal(4m, afterReplay.Single().AvailableQuantity);
        }
        finally { SqliteConnection.ClearAllPools(); DeleteTemporaryRoot(root); }
    }

    [Fact]
    public async Task Return_quantity_cannot_exceed_remaining_sold_quantity()
    {
        var root = CreateTemporaryRoot();
        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);
            var setup = await CreateSaleAsync(provider, 3m, 20m, 0m);
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var line = Assert.Single((await returns.GetReturnableSaleAsync(setup.Sale.Sale.Id))!.Lines);
            await returns.ProcessAsync(new ProcessSaleReturnRequest(setup.Sale.Sale.Id, "partial-return-1", "Partial return",
                [new ProcessSaleReturnLineRequest(line.Id, 2m)], [new ProcessSaleReturnRefundRequest("cash", 40m)]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => returns.ProcessAsync(new ProcessSaleReturnRequest(setup.Sale.Sale.Id, "partial-return-2", "Too many",
                [new ProcessSaleReturnLineRequest(line.Id, 2m)], [new ProcessSaleReturnRefundRequest("cash", 40m)])));
            Assert.Equal(1m, Assert.Single((await returns.GetReturnableSaleAsync(setup.Sale.Sale.Id))!.Lines).RemainingQuantity);
        }
        finally { SqliteConnection.ClearAllPools(); DeleteTemporaryRoot(root); }
    }

    [Fact]
    public async Task Refund_mismatch_rolls_back_return_and_stock()
    {
        var root = CreateTemporaryRoot();
        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);
            var setup = await CreateSaleAsync(provider, 2m, 25m, 0m);
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var inventory = provider.GetRequiredService<IInventoryService>();
            var line = Assert.Single((await returns.GetReturnableSaleAsync(setup.Sale.Sale.Id))!.Lines);
            await Assert.ThrowsAsync<InvalidOperationException>(() => returns.ProcessAsync(new ProcessSaleReturnRequest(setup.Sale.Sale.Id, "bad-refund", "Mismatch",
                [new ProcessSaleReturnLineRequest(line.Id, 1m)], [new ProcessSaleReturnRefundRequest("cash", 10m)])));
            var batches = await inventory.SearchBatchesAsync(new InventoryBatchFilter(StockLocationId: setup.LocationId));
            Assert.Equal(3m, batches.Single().AvailableQuantity);
            Assert.Empty(await returns.SearchReturnsAsync(new SaleReturnSearchFilter(Search: setup.Sale.Sale.SaleNumber)));
        }
        finally { SqliteConnection.ClearAllPools(); DeleteTemporaryRoot(root); }
    }

    private static async Task<(SaleDetail Sale, string LocationId)> CreateSaleAsync(ServiceProvider provider, decimal quantity, decimal price, decimal discount)
    {
        var medicines = provider.GetRequiredService<IMedicineCatalogService>();
        var medicineId = await medicines.CreateAsync(new SaveMedicineRequest(null, null, $"RET-{Guid.NewGuid():N}"[..16], null, "Return Test Medicine", null, "500mg", "tablet", "pack", "tablet", 10m, 0m, false, true, true, true, null));
        var inventory = provider.GetRequiredService<IInventoryService>();
        await inventory.EnsureDefaultsAsync();
        var location = (await inventory.GetReferenceDataAsync()).Locations.Single();
        await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(medicineId, location.Id, "RET-BATCH", null, new DateOnly(2028, 1, 1), 5m, 10m, price, null));
        var pos = provider.GetRequiredService<IPosService>();
        var grand = decimal.Round((quantity * price) - discount, 4, MidpointRounding.AwayFromZero);
        var sale = await pos.CheckoutAsync(new PosCheckoutRequest(location.Id, null, Guid.NewGuid().ToString("N"), null, null, null, null,
            [new PosCheckoutLineRequest(medicineId, quantity, DiscountAmount: discount)], [new PosPaymentRequest("cash", grand)]));
        return (sale, location.Id);
    }

    private static async Task InitializeAsync(ServiceProvider provider) => await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync("tenant-returns");
    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        services.AddBusinessOSInfrastructure(new ApplicationPaths(root));
        services.AddSingleton<IPermissionAuthorizer>(new AllowAllPermissionAuthorizer());
        services.AddSingleton<IUserSessionService>(new TestUserSessionService());
        services.AddBusinessOSPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }
    private static string CreateTemporaryRoot() => Path.Combine(Path.GetTempPath(), "darmaltoon-return-tests", Guid.NewGuid().ToString("N"));
    private static void DeleteTemporaryRoot(string root) { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    private sealed class AllowAllPermissionAuthorizer : IPermissionAuthorizer { public bool HasPermission(string permission) => true; public void Demand(string permission) { } }
    private sealed class TestUserSessionService : IUserSessionService
    {
        private static readonly UserSessionSnapshot Session = new("user-returns", "tenant-returns", "activation-returns", "device-returns", "Returns Tester", "returns@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cashier" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pos.sell", "returns.manage", "pos.discount", "inventory.manage", "medicines.manage" },
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(8));
        public UserSessionSnapshot? Current => Session;
        public Task<UserSessionSnapshot> LoginAsync(string email, string password, bool allowOfflineSignIn, CancellationToken cancellationToken = default) => Task.FromResult(Session);
        public Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(Session);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
