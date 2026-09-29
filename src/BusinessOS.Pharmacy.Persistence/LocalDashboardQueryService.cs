using System.Data;
using System.Data.Common;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalDashboardQueryService : ILocalDashboardQueryService
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;

    public LocalDashboardQueryService(IDbContextFactory<PharmacyDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(
        DashboardQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var hasSales = await TableExistsAsync(connection, "sales", cancellationToken);
        var hasCustomers = await TableExistsAsync(connection, "customers", cancellationToken);
        var hasMedicines = await TableExistsAsync(connection, "medicines", cancellationToken);
        var hasBatches = await TableExistsAsync(connection, "product_batches", cancellationToken);
        var hasLocations = await TableExistsAsync(connection, "stock_locations", cancellationToken);
        var hasInventory = hasMedicines && hasBatches;

        decimal todaySales = 0;
        int todayTransactions = 0;
        decimal outstandingCredit = 0;

        if (hasSales)
        {
            todaySales = await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(grand_total AS NUMERIC)), 0)
                FROM sales
                WHERE status = 'completed' AND date(business_date) = date($businessDate);
                """,
                [("$businessDate", options.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))],
                cancellationToken);

            todayTransactions = await ScalarIntAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM sales
                WHERE status = 'completed' AND date(business_date) = date($businessDate);
                """,
                [("$businessDate", options.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))],
                cancellationToken);

            outstandingCredit = await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(due_total AS NUMERIC)), 0)
                FROM sales
                WHERE status = 'completed';
                """,
                [],
                cancellationToken);
        }

        var activeCustomers = hasCustomers
            ? await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM customers WHERE is_active = 1;",
                [],
                cancellationToken)
            : 0;

        decimal stockValue = 0;
        int lowStock = 0;
        int nearExpiry = 0;
        int expired = 0;
        var alerts = new List<DashboardAlertItem>();

        if (hasInventory)
        {
            var businessDate = options.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var nearExpiryEnd = options.BusinessDate
                .AddDays(options.NearExpiryDays)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            stockValue = await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(
                    CAST(available_quantity AS NUMERIC) * CAST(purchase_cost AS NUMERIC)
                ), 0)
                FROM product_batches
                WHERE status = 'active' AND CAST(available_quantity AS NUMERIC) > 0;
                """,
                [],
                cancellationToken);

            lowStock = await ScalarIntAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM (
                    SELECT
                        m.id,
                        COALESCE(SUM(
                            CASE
                                WHEN pb.status = 'active'
                                 AND CAST(pb.available_quantity AS NUMERIC) > 0
                                 AND (pb.expires_at IS NULL OR date(pb.expires_at) >= date($businessDate))
                                THEN CAST(pb.available_quantity AS NUMERIC)
                                ELSE 0
                            END
                        ), 0) AS available_quantity,
                        CASE
                            WHEN CAST(m.reorder_level AS NUMERIC) > 0
                            THEN CAST(m.reorder_level AS NUMERIC)
                            ELSE $lowStockThreshold
                        END AS threshold
                    FROM medicines m
                    LEFT JOIN product_batches pb ON pb.medicine_id = m.id
                    WHERE m.is_active = 1
                    GROUP BY m.id, m.reorder_level
                ) inventory
                WHERE available_quantity <= threshold;
                """,
                [
                    ("$businessDate", businessDate),
                    ("$lowStockThreshold", options.LowStockThreshold),
                ],
                cancellationToken);

            nearExpiry = await ScalarIntAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM product_batches
                WHERE status = 'active'
                  AND CAST(available_quantity AS NUMERIC) > 0
                  AND expires_at IS NOT NULL
                  AND date(expires_at) >= date($businessDate)
                  AND date(expires_at) <= date($nearExpiryEnd);
                """,
                [
                    ("$businessDate", businessDate),
                    ("$nearExpiryEnd", nearExpiryEnd),
                ],
                cancellationToken);

            expired = await ScalarIntAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM product_batches
                WHERE status = 'active'
                  AND CAST(available_quantity AS NUMERIC) > 0
                  AND expires_at IS NOT NULL
                  AND date(expires_at) < date($businessDate);
                """,
                [("$businessDate", businessDate)],
                cancellationToken);

            alerts.AddRange(await ReadLowStockAlertsAsync(
                connection,
                businessDate,
                options.LowStockThreshold,
                cancellationToken));

            alerts.AddRange(await ReadExpiryAlertsAsync(
                connection,
                businessDate,
                nearExpiryEnd,
                hasLocations,
                cancellationToken));
        }

        return new DashboardSnapshot(
            options.BusinessDate,
            todaySales,
            lowStock,
            nearExpiry,
            expired,
            todayTransactions,
            stockValue,
            activeCustomers,
            outstandingCredit,
            alerts,
            new DashboardDataAvailability(hasSales, hasInventory, hasCustomers));
    }

    private static async Task<IReadOnlyList<DashboardAlertItem>> ReadLowStockAlertsAsync(
        DbConnection connection,
        string businessDate,
        int lowStockThreshold,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT medicine_code, brand_name, available_quantity, threshold
            FROM (
                SELECT
                    m.id,
                    m.medicine_code,
                    m.brand_name,
                    COALESCE(SUM(
                        CASE
                            WHEN pb.status = 'active'
                             AND CAST(pb.available_quantity AS NUMERIC) > 0
                             AND (pb.expires_at IS NULL OR date(pb.expires_at) >= date($businessDate))
                            THEN CAST(pb.available_quantity AS NUMERIC)
                            ELSE 0
                        END
                    ), 0) AS available_quantity,
                    CASE
                        WHEN CAST(m.reorder_level AS NUMERIC) > 0
                        THEN CAST(m.reorder_level AS NUMERIC)
                        ELSE $lowStockThreshold
                    END AS threshold
                FROM medicines m
                LEFT JOIN product_batches pb ON pb.medicine_id = m.id
                WHERE m.is_active = 1
                GROUP BY m.id, m.medicine_code, m.brand_name, m.reorder_level
            ) inventory
            WHERE available_quantity <= threshold
            ORDER BY available_quantity, brand_name
            LIMIT 5;
            """;
        AddParameter(command, "$businessDate", businessDate);
        AddParameter(command, "$lowStockThreshold", lowStockThreshold);

        var result = new List<DashboardAlertItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DashboardAlertItem(
                "low_stock",
                reader.GetString(1),
                reader.IsDBNull(0) ? null : reader.GetString(0),
                null,
                null,
                ReadDecimal(reader, 2),
                ReadDecimal(reader, 3),
                null));
        }

        return result;
    }

    private static async Task<IReadOnlyList<DashboardAlertItem>> ReadExpiryAlertsAsync(
        DbConnection connection,
        string businessDate,
        string nearExpiryEnd,
        bool hasLocations,
        CancellationToken cancellationToken)
    {
        var locationExpression = hasLocations
            ? "sl.name"
            : "NULL";

        var locationJoin = hasLocations
            ? "LEFT JOIN stock_locations sl ON sl.id = pb.stock_location_id"
            : string.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                CASE WHEN date(pb.expires_at) < date($businessDate)
                     THEN 'expired' ELSE 'near_expiry' END AS kind,
                m.brand_name,
                m.medicine_code,
                pb.batch_number,
                {locationExpression} AS location_name,
                pb.available_quantity,
                pb.expires_at
            FROM product_batches pb
            INNER JOIN medicines m ON m.id = pb.medicine_id
            {locationJoin}
            WHERE pb.status = 'active'
              AND CAST(pb.available_quantity AS NUMERIC) > 0
              AND pb.expires_at IS NOT NULL
              AND date(pb.expires_at) <= date($nearExpiryEnd)
            ORDER BY date(pb.expires_at), m.brand_name
            LIMIT 10;
            """;
        AddParameter(command, "$businessDate", businessDate);
        AddParameter(command, "$nearExpiryEnd", nearExpiryEnd);

        var result = new List<DashboardAlertItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var expiry = DateOnly.TryParse(
                reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
                ? parsed
                : (DateOnly?)null;

            result.Add(new DashboardAlertItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ReadDecimal(reader, 5),
                null,
                expiry));
        }

        return result;
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName;
            """;
        AddParameter(command, "$tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<decimal> ScalarDecimalAsync(
        DbConnection connection,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            AddParameter(command, parameter.Name, parameter.Value);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return ToDecimal(value);
    }

    private static async Task<int> ScalarIntAsync(
        DbConnection connection,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            AddParameter(command, parameter.Name, parameter.Value);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static decimal ReadDecimal(DbDataReader reader, int ordinal) =>
        ToDecimal(reader.GetValue(ordinal));

    private static decimal ToDecimal(object? value)
    {
        if (value is null or DBNull)
        {
            return 0;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        return decimal.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }
}
