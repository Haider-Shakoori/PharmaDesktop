using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class PharmacyReportTests
{
    [Fact]
    public async Task Summary_matches_sales_returns_cogs_collections_and_stock_value()
    {
        var root = CreateTemporaryRoot();
        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var medicineId = await CreateMedicineAsync(provider, 10m);
            var inventory = provider.GetRequiredService<IInventoryService>();
            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            await inventory.CreateOpeningStockAsync(new CreateOpeningStockRequest(
                medicineId,
                location.Id,
                "REPORT-B",
                null,
                new DateOnly(2026, 12, 15),
                5m,
                40m,
                100m,
                null));

            var pos = provider.GetRequiredService<IPosService>();
            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                location.Id,
                null,
                "report-sale-1",
                null,
                null,
                null,
                null,
                [new PosCheckoutLineRequest(medicineId, 1m, DiscountAmount: 10m)],
                [new PosPaymentRequest("cash", 90m)]));

            var returns = provider.GetRequiredService<ISaleReturnService>();
            var saleLine = Assert.Single((await returns.GetReturnableSaleAsync(sale.Sale.Id))!.Lines);
            await returns.ProcessAsync(new ProcessSaleReturnRequest(
                sale.Sale.Id,
                "report-return-1",
                "Partial return",
                [new ProcessSaleReturnLineRequest(saleLine.Id, 0.2m)],
                [new ProcessSaleReturnRefundRequest("cash", 18m)]));

            var reports = provider.GetRequiredService<IPharmacyReportService>();
            var currentDate = sale.Sale.BusinessDate;
            var report = await reports.GetAsync(new ReportRange(currentDate, currentDate));

            Assert.Equal(90m, report.Summary.Sales);
            Assert.Equal(18m, report.Summary.Returns);
            Assert.Equal(72m, report.Summary.NetSales);
            Assert.Equal(10m, report.Summary.Discounts);
            Assert.Equal(40m, report.Summary.GrossProfit);
            Assert.Equal(72m, report.Summary.Collections);
            Assert.Equal(0m, report.Summary.Purchases);
            Assert.Equal(168m, report.Summary.StockValue);
            Assert.Single(report.Sales);
            Assert.Single(report.Returns);
            Assert.Contains(report.LowStock, x => x.MedicineId == medicineId && x.AvailableStock == 4.2m);
            var nearExpiry = Assert.Single(report.NearExpiry);\n            Assert.Equal("REPORT-B", nearExpiry.BatchNumber);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Range_swaps_reversed_dates_and_csv_uses_expected_headers()
    {
        var root = CreateTemporaryRoot();
        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var reports = provider.GetRequiredService<IPharmacyReportService>();
            var range = reports.ResolveRange(
                new DateOnly(2026, 9, 30),
                new DateOnly(2026, 9, 1));

            Assert.Equal(new DateOnly(2026, 9, 1), range.From);
            Assert.Equal(new DateOnly(2026, 9, 30), range.To);

            var csv = await reports.BuildCsvAsync("sales", range);
            Assert.Equal("sales-2026-09-01-to-2026-09-30.csv", csv.FileName);
            Assert.StartsWith(
                "Sale Number,Business Date,Total,Discount,Paid,Due,Payment Status",
                csv.Content);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task<string> CreateMedicineAsync(
        ServiceProvider provider,
        decimal reorderLevel)
    {
        var medicines = provider.GetRequiredService<IMedicineCatalogService>();
        return await medicines.CreateAsync(new SaveMedicineRequest(
            null,
            null,
            $"RPT-{Guid.NewGuid():N}"[..16],
            null,
            "Report Medicine",
            null,
            "500mg",
            "tablet",
            "pack",
            "tablet",
            1m,
            reorderLevel,
            false,
            true,
            true,
            true,
            null));
    }

    private static async Task InitializeAsync(ServiceProvider provider) =>
        await provider.GetRequiredService<ILocalDatabaseInitializer>()
            .InitializeAsync("tenant-reports");

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        services.AddBusinessOSInfrastructure(new ApplicationPaths(root));
        services.AddSingleton<IPermissionAuthorizer>(new AllowAllPermissionAuthorizer());
        services.AddSingleton<IUserSessionService>(new TestUserSessionService());
        services.AddBusinessOSPersistence();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-report-tests",
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
            "report-user",
            "tenant-reports",
            "activation",
            "device",
            "Report Tester",
            "report@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "reports.view",
                "pos.sell",
                "pos.discount",
                "returns.manage",
                "inventory.manage",
                "medicines.manage",
            },
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(8));

        public UserSessionSnapshot? Current => Session;
        public Task<UserSessionSnapshot> LoginAsync(string email, string password, bool allowOfflineSignIn, CancellationToken cancellationToken = default) => Task.FromResult(Session);
        public Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(Session);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
