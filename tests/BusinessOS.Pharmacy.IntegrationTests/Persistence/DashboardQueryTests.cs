using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class DashboardQueryTests
{
    [Fact]
    public async Task Dashboard_returns_clean_zero_state_before_operational_module_tables_exist()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-dashboard");

            var dashboard = provider.GetRequiredService<ILocalDashboardQueryService>();
            var snapshot = await dashboard.GetSnapshotAsync(
                new DashboardQueryOptions(new DateOnly(2026, 9, 30)));

            Assert.Equal(0m, snapshot.TodaySales);
            Assert.Equal(0, snapshot.TodayTransactions);
            Assert.Equal(0m, snapshot.StockValue);
            Assert.Equal(0, snapshot.ActiveCustomers);
            Assert.Equal(0m, snapshot.OutstandingCredit);
            Assert.Equal(0, snapshot.TotalAlerts);
            Assert.False(snapshot.Availability.Sales);
            Assert.False(snapshot.Availability.Inventory);
            Assert.False(snapshot.Availability.Customers);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Dashboard_matches_Laravel_operational_metrics_from_local_tables()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            var paths = provider.GetRequiredService<ApplicationPaths>();
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-dashboard");

            await SeedOperationalTablesAsync(paths.DatabasePath);

            var dashboard = provider.GetRequiredService<ILocalDashboardQueryService>();
            var snapshot = await dashboard.GetSnapshotAsync(
                new DashboardQueryOptions(
                    new DateOnly(2026, 9, 30),
                    LowStockThreshold: 10,
                    NearExpiryDays: 90));

            Assert.Equal(150m, snapshot.TodaySales);
            Assert.Equal(2, snapshot.TodayTransactions);
            Assert.Equal(72m, snapshot.StockValue);
            Assert.Equal(2, snapshot.ActiveCustomers);
            Assert.Equal(25m, snapshot.OutstandingCredit);

            Assert.Equal(2, snapshot.LowStockCount);
            Assert.Equal(1, snapshot.NearExpiryCount);
            Assert.Equal(1, snapshot.ExpiredCount);
            Assert.Equal(4, snapshot.TotalAlerts);

            Assert.True(snapshot.Availability.Sales);
            Assert.True(snapshot.Availability.Inventory);
            Assert.True(snapshot.Availability.Customers);

            Assert.Contains(snapshot.Alerts, item =>
                item.Kind == "low_stock" &&
                item.Name == "Alpha" &&
                item.AvailableQuantity == 5m &&
                item.Threshold == 10m);

            Assert.Contains(snapshot.Alerts, item =>
                item.Kind == "near_expiry" &&
                item.BatchNumber == "NEAR-001");

            Assert.Contains(snapshot.Alerts, item =>
                item.Kind == "expired" &&
                item.BatchNumber == "EXP-001");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task SeedOperationalTablesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE medicines (
                id TEXT PRIMARY KEY,
                medicine_code TEXT NOT NULL,
                brand_name TEXT NOT NULL,
                reorder_level NUMERIC NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE stock_locations (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL
            );

            CREATE TABLE product_batches (
                id TEXT PRIMARY KEY,
                medicine_id TEXT NOT NULL,
                stock_location_id TEXT NOT NULL,
                batch_number TEXT NULL,
                status TEXT NOT NULL,
                available_quantity NUMERIC NOT NULL,
                purchase_cost NUMERIC NOT NULL,
                expires_at TEXT NULL
            );

            CREATE TABLE sales (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                business_date TEXT NOT NULL,
                grand_total NUMERIC NOT NULL,
                due_total NUMERIC NOT NULL
            );

            CREATE TABLE customers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            INSERT INTO stock_locations (id, name)
            VALUES ('loc-1', 'Main Store');

            INSERT INTO medicines (id, medicine_code, brand_name, reorder_level, is_active)
            VALUES
                ('med-1', 'MED-001', 'Alpha', 10, 1),
                ('med-2', 'MED-002', 'Beta', 0, 1),
                ('med-3', 'MED-003', 'Gamma', 0, 1);

            INSERT INTO product_batches (
                id, medicine_id, stock_location_id, batch_number, status,
                available_quantity, purchase_cost, expires_at
            )
            VALUES
                ('batch-1', 'med-1', 'loc-1', 'NEAR-001', 'active', 5, 2, '2026-10-10'),
                ('batch-2', 'med-2', 'loc-1', 'FAR-001', 'active', 20, 3, '2026-12-31'),
                ('batch-3', 'med-2', 'loc-1', 'EXP-001', 'active', 2, 1, '2026-09-29');

            INSERT INTO sales (id, status, business_date, grand_total, due_total)
            VALUES
                ('sale-1', 'completed', '2026-09-30', 100, 20),
                ('sale-2', 'completed', '2026-09-30', 50, 0),
                ('sale-3', 'completed', '2026-09-29', 30, 5),
                ('sale-4', 'held', '2026-09-30', 999, 999);

            INSERT INTO customers (id, name, is_active)
            VALUES
                ('customer-1', 'Customer One', 1),
                ('customer-2', 'Customer Two', 1),
                ('customer-3', 'Inactive Customer', 0);
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "businessos-pharmacy-dashboard-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
