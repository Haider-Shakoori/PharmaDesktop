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
    public async Task Dashboard_returns_zero_metrics_when_inventory_is_initialized_but_empty()
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
            Assert.True(snapshot.Availability.Sales);
            Assert.True(snapshot.Availability.Inventory);
            Assert.True(snapshot.Availability.Customers);
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
            INSERT INTO branches (
                id, code, name, is_default, is_active, created_at, updated_at
            )
            VALUES (
                'branch-1', 'MAIN', 'Main Branch', 1, 1,
                '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'
            );

            INSERT INTO stock_locations (
                id, branch_id, code, name, kind, is_default, is_active, created_at, updated_at
            )
            VALUES (
                'loc-1', 'branch-1', 'MAIN', 'Main Store', 'store', 1, 1,
                '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'
            );

            INSERT INTO medicines (
                id, medicine_code, brand_name, purchase_unit, sale_unit,
                units_per_purchase_unit, reorder_level, prescription_required,
                batch_tracking_required, expiry_tracking_required, is_active,
                created_at, updated_at
            )
            VALUES
                ('med-1', 'MED-001', 'Alpha', 'pack', 'unit', 1, 10, 0, 1, 1, 1, '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'),
                ('med-2', 'MED-002', 'Beta', 'pack', 'unit', 1, 0, 0, 1, 1, 1, '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'),
                ('med-3', 'MED-003', 'Gamma', 'pack', 'unit', 1, 0, 0, 1, 1, 1, '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00');

            INSERT INTO product_batches (
                id, medicine_id, branch_id, stock_location_id, batch_number, batch_key,
                expires_at, status, received_quantity, available_quantity,
                purchase_cost, sale_price, created_at, updated_at
            )
            VALUES
                ('batch-1', 'med-1', 'branch-1', 'loc-1', 'NEAR-001', 'key-1',
                 '2026-10-10', 'active', 5, 5, 2, 3,
                 '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'),
                ('batch-2', 'med-2', 'branch-1', 'loc-1', 'FAR-001', 'key-2',
                 '2026-12-31', 'active', 20, 20, 3, 4,
                 '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'),
                ('batch-3', 'med-2', 'branch-1', 'loc-1', 'EXP-001', 'key-3',
                 '2026-09-29', 'active', 2, 2, 1, 2,
                 '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00');

            INSERT INTO customers (
                id, name, credit_limit, is_active, created_at, updated_at
            )
            VALUES
                ('customer-1', 'Customer One', 100, 1, '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'),
                ('customer-2', 'Customer Two', 0, 1, '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00'),
                ('customer-3', 'Inactive Customer', 0, 0, '2026-09-30T00:00:00+00:00', '2026-09-30T00:00:00+00:00');

            INSERT INTO sales (
                id, sale_number, stock_location_id, customer_id, business_date, status, currency,
                subtotal, discount_total, tax_total, grand_total, paid_total, due_total, change_total,
                payment_status, idempotency_key, created_by, created_at, updated_at, completed_at
            )
            VALUES
                ('sale-1', 'POS-1', 'loc-1', 'customer-1', '2026-09-30', 'completed', 'AFN', 100, 0, 0, 100, 80, 20, 0, 'partial', 'dash-1', 'u1', '2026-09-30T08:00:00+00:00', '2026-09-30T08:00:00+00:00', '2026-09-30T08:00:00+00:00'),
                ('sale-2', 'POS-2', 'loc-1', 'customer-2', '2026-09-30', 'completed', 'AFN', 50, 0, 0, 50, 50, 0, 0, 'paid', 'dash-2', 'u1', '2026-09-30T09:00:00+00:00', '2026-09-30T09:00:00+00:00', '2026-09-30T09:00:00+00:00'),
                ('sale-3', 'POS-3', 'loc-1', 'customer-1', '2026-09-29', 'completed', 'AFN', 30, 0, 0, 30, 25, 5, 0, 'partial', 'dash-3', 'u1', '2026-09-29T09:00:00+00:00', '2026-09-29T09:00:00+00:00', '2026-09-29T09:00:00+00:00'),
                ('sale-4', 'POS-4', 'loc-1', NULL, '2026-09-30', 'held', 'AFN', 999, 0, 0, 999, 0, 999, 0, 'unpaid', 'dash-4', 'u1', '2026-09-30T10:00:00+00:00', '2026-09-30T10:00:00+00:00', NULL);
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
