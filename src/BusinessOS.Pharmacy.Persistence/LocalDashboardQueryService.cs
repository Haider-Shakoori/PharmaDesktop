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
        var hasSaleLines = await TableExistsAsync(connection, "sale_lines", cancellationToken);
        var hasSalePayments = await TableExistsAsync(connection, "sale_payments", cancellationToken);
        var hasCustomers = await TableExistsAsync(connection, "customers", cancellationToken);
        var hasMedicines = await TableExistsAsync(connection, "medicines", cancellationToken);
        var hasBatches = await TableExistsAsync(connection, "product_batches", cancellationToken);
        var hasLocations = await TableExistsAsync(connection, "stock_locations", cancellationToken);
        var hasPurchaseInvoices = await TableExistsAsync(connection, "purchase_invoices", cancellationToken);
        var hasSuppliers = await TableExistsAsync(connection, "suppliers", cancellationToken);
        var hasGoodsReceiptLines = await TableExistsAsync(connection, "goods_receipt_lines", cancellationToken);
        var hasCashierShifts = await TableExistsAsync(connection, "cashier_shifts", cancellationToken);
        var hasDailyClosings = await TableExistsAsync(connection, "daily_closings", cancellationToken);
        var hasSaleReturns = await TableExistsAsync(connection, "sale_returns", cancellationToken);
        var hasSaleReturnRefunds = await TableExistsAsync(connection, "sale_return_refunds", cancellationToken);
        var hasInventory = hasMedicines && hasBatches;

        var businessDate = options.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var monthStartDate = new DateOnly(options.BusinessDate.Year, options.BusinessDate.Month, 1);
        var nextMonthDate = monthStartDate.AddMonths(1);
        var monthStart = monthStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var nextMonth = nextMonthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        decimal todaySales = 0;
        int todayTransactions = 0;
        decimal outstandingCredit = 0;
        decimal monthSales = 0;

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

            monthSales = await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(grand_total AS NUMERIC)), 0)
                FROM sales
                WHERE status = 'completed'
                  AND date(business_date) >= date($monthStart)
                  AND date(business_date) < date($nextMonth);
                """,
                [("$monthStart", monthStart), ("$nextMonth", nextMonth)],
                cancellationToken);
        }

        decimal todayPurchases = 0;
        int todayPurchaseCount = 0;
        decimal monthPurchases = 0;

        if (hasPurchaseInvoices)
        {
            todayPurchases = await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(grand_total AS NUMERIC)), 0)
                FROM purchase_invoices
                WHERE status <> 'cancelled'
                  AND date(invoice_date) = date($businessDate);
                """,
                [("$businessDate", businessDate)],
                cancellationToken);

            todayPurchaseCount = await ScalarIntAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM purchase_invoices
                WHERE status <> 'cancelled'
                  AND date(invoice_date) = date($businessDate);
                """,
                [("$businessDate", businessDate)],
                cancellationToken);

            monthPurchases = await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(grand_total AS NUMERIC)), 0)
                FROM purchase_invoices
                WHERE status <> 'cancelled'
                  AND date(invoice_date) >= date($monthStart)
                  AND date(invoice_date) < date($nextMonth);
                """,
                [("$monthStart", monthStart), ("$nextMonth", nextMonth)],
                cancellationToken);
        }

        var activeCustomers = hasCustomers
            ? await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM customers WHERE is_active = 1;",
                [],
                cancellationToken)
            : 0;

        var totalMedicines = hasMedicines
            ? await ScalarIntAsync(connection, "SELECT COUNT(*) FROM medicines WHERE is_active = 1;", [], cancellationToken)
            : 0;
        var totalBatches = hasBatches
            ? await ScalarIntAsync(connection, "SELECT COUNT(*) FROM product_batches;", [], cancellationToken)
            : 0;
        var totalSuppliers = hasSuppliers
            ? await ScalarIntAsync(connection, "SELECT COUNT(*) FROM suppliers WHERE is_active = 1;", [], cancellationToken)
            : 0;

        decimal stockValue = 0;
        int lowStock = 0;
        int nearExpiry = 0;
        int expired = 0;
        var alerts = new List<DashboardAlertItem>();

        if (hasInventory)
        {
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

        var lowStockItems = alerts
            .Where(x => x.Kind == "low_stock")
            .Take(5)
            .Select(x => new DashboardLowStockItem(
                x.Name,
                x.AvailableQuantity,
                x.Threshold ?? options.LowStockThreshold,
                "Low Stock"))
            .ToList();

        var expiryItems = alerts
            .Where(x => x.Kind == "near_expiry" && x.ExpiresAt is not null)
            .Take(5)
            .Select(x =>
            {
                var expiryDate = x.ExpiresAt!.Value;
                return new DashboardExpiryItem(
                    x.Name,
                    x.BatchNumber ?? "—",
                    expiryDate,
                    expiryDate.DayNumber - options.BusinessDate.DayNumber,
                    "Expiring");
            })
            .ToList();

        var salesTimeline = hasSales
            ? await ReadSalesTimelineAsync(connection, businessDate, options.Period, cancellationToken)
            : Enumerable.Range(8, 12)
                .Select(hour => new DashboardSalesPoint(hour, 0m, 0))
                .ToList();

        var recentTransactions = new List<DashboardTransactionItem>();
        if (hasSales)
        {
            recentTransactions.AddRange(await ReadSaleTransactionsAsync(
                connection,
                hasSaleLines,
                hasSalePayments,
                cancellationToken));
        }

        if (hasPurchaseInvoices)
        {
            recentTransactions.AddRange(await ReadPurchaseTransactionsAsync(
                connection,
                hasSuppliers,
                hasGoodsReceiptLines,
                cancellationToken));
        }

        recentTransactions = recentTransactions
            .OrderByDescending(x => x.OccurredAt)
            .Take(5)
            .ToList();

        var (cashInDrawer, expectedCash) = await ReadCashPositionAsync(
            connection,
            businessDate,
            hasSales,
            hasSalePayments,
            hasCashierShifts,
            hasDailyClosings,
            hasSaleReturns,
            hasSaleReturnRefunds,
            cancellationToken);

        return new DashboardSnapshot(
            options.BusinessDate,
            todaySales,
            todayPurchases,
            todayPurchaseCount,
            lowStock,
            nearExpiry,
            expired,
            todayTransactions,
            cashInDrawer,
            expectedCash,
            stockValue,
            activeCustomers,
            outstandingCredit,
            totalMedicines,
            totalBatches,
            totalSuppliers,
            monthSales,
            monthPurchases,
            alerts,
            lowStockItems,
            expiryItems,
            recentTransactions,
            salesTimeline,
            new DashboardDataAvailability(
                hasSales,
                hasInventory,
                hasCustomers,
                hasPurchaseInvoices,
                hasSuppliers,
                hasSales && hasSalePayments));
    }

    private static async Task<(decimal CashInDrawer, decimal ExpectedCash)> ReadCashPositionAsync(
        DbConnection connection,
        string businessDate,
        bool hasSales,
        bool hasSalePayments,
        bool hasCashierShifts,
        bool hasDailyClosings,
        bool hasSaleReturns,
        bool hasSaleReturnRefunds,
        CancellationToken cancellationToken)
    {
        if (!hasSales)
        {
            return (0m, 0m);
        }

        var openingCash = hasCashierShifts
            ? await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(opening_cash AS NUMERIC)), 0)
                FROM cashier_shifts
                WHERE date(business_date) = date($businessDate);
                """,
                [("$businessDate", businessDate)],
                cancellationToken)
            : 0m;

        var cashCollected = hasSalePayments
            ? await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(sp.amount AS NUMERIC)), 0)
                FROM sale_payments sp
                INNER JOIN sales s ON s.id = sp.sale_id
                WHERE sp.method = 'cash'
                  AND s.status = 'completed'
                  AND date(s.business_date) = date($businessDate);
                """,
                [("$businessDate", businessDate)],
                cancellationToken)
            : 0m;

        var changeGiven = await ScalarDecimalAsync(
            connection,
            """
            SELECT COALESCE(SUM(CAST(change_total AS NUMERIC)), 0)
            FROM sales
            WHERE status = 'completed'
              AND date(business_date) = date($businessDate);
            """,
            [("$businessDate", businessDate)],
            cancellationToken);

        var cashRefunds = hasSaleReturns && hasSaleReturnRefunds
            ? await ScalarDecimalAsync(
                connection,
                """
                SELECT COALESCE(SUM(CAST(rr.amount AS NUMERIC)), 0)
                FROM sale_return_refunds rr
                INNER JOIN sale_returns sr ON sr.id = rr.sale_return_id
                WHERE rr.method = 'cash'
                  AND sr.status = 'completed'
                  AND date(sr.business_date) = date($businessDate);
                """,
                [("$businessDate", businessDate)],
                cancellationToken)
            : 0m;

        var expected = openingCash + cashCollected - changeGiven - cashRefunds;

        if (!hasDailyClosings)
        {
            return (expected, expected);
        }

        var counted = await ScalarNullableDecimalAsync(
            connection,
            """
            SELECT counted_cash
            FROM daily_closings
            WHERE date(business_date) = date($businessDate)
              AND counted_cash IS NOT NULL
            ORDER BY updated_at DESC
            LIMIT 1;
            """,
            [("$businessDate", businessDate)],
            cancellationToken);

        return (counted ?? expected, expected);
    }

    private static async Task<IReadOnlyList<DashboardSalesPoint>> ReadSalesTimelineAsync(
        DbConnection connection,
        string businessDate,
        string period,
        CancellationToken cancellationToken)
    {
        if (period is "week" or "month" or "year")
        {
            return await ReadPeriodTimelineAsync(connection, businessDate, period, cancellationToken);
        }

        var byHour = new Dictionary<int, (decimal Sales, int Invoices)>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(completed_at, created_at) AS occurred_at,
                grand_total
            FROM sales
            WHERE status = 'completed'
              AND date(business_date) = date($businessDate)
            ORDER BY occurred_at;
            """;
        AddParameter(command, "$businessDate", businessDate);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var occurredAt = ReadDateTimeOffset(reader, 0);
            if (occurredAt == DateTimeOffset.MinValue)
            {
                continue;
            }

            // completed_at/created_at are stored as absolute timestamps. Group the
            // dashboard graph by the workstation's local hour so the chart matches
            // the times shown in Recent Transactions instead of silently using UTC.
            var localHour = occurredAt.ToLocalTime().Hour;
            var existing = byHour.GetValueOrDefault(localHour);
            byHour[localHour] = (
                existing.Sales + ReadDecimal(reader, 1),
                existing.Invoices + 1);
        }

        return Enumerable.Range(0, 24)
            .Select(hour =>
            {
                var value = byHour.GetValueOrDefault(hour);
                return new DashboardSalesPoint(hour, value.Sales, value.Invoices);
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<DashboardSalesPoint>> ReadPeriodTimelineAsync(
        DbConnection connection,
        string businessDate,
        string period,
        CancellationToken cancellationToken)
    {
        var anchor = DateOnly.ParseExact(businessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (start, end) = period switch
        {
            "week" => (anchor.AddDays(-6), anchor),
            "month" => (new DateOnly(anchor.Year, anchor.Month, 1),
                new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1)),
            _ => (new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year, 12, 31)),
        };

        var byKey = new Dictionary<string, (decimal Sales, int Invoices)>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = period == "year"
                ? """
                  SELECT
                      substr(business_date, 1, 7) AS bucket,
                      COALESCE(SUM(CAST(grand_total AS NUMERIC)), 0) AS sales,
                      COUNT(*) AS invoices
                  FROM sales
                  WHERE status = 'completed'
                    AND date(business_date) >= date($start)
                    AND date(business_date) <= date($end)
                  GROUP BY bucket
                  ORDER BY bucket;
                  """
                : """
                  SELECT
                      date(business_date) AS bucket,
                      COALESCE(SUM(CAST(grand_total AS NUMERIC)), 0) AS sales,
                      COUNT(*) AS invoices
                  FROM sales
                  WHERE status = 'completed'
                    AND date(business_date) >= date($start)
                    AND date(business_date) <= date($end)
                  GROUP BY bucket
                  ORDER BY bucket;
                  """;
            AddParameter(command, "$start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AddParameter(command, "$end", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                byKey[reader.GetString(0)] = (ReadDecimal(reader, 1), (int)reader.GetInt64(2));
            }
        }

        var points = new List<DashboardSalesPoint>();
        if (period == "year")
        {
            for (var monthIndex = 0; monthIndex < 12; monthIndex++)
            {
                var month = start.AddMonths(monthIndex);
                var key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var value = byKey.GetValueOrDefault(key);
                points.Add(new DashboardSalesPoint(
                    monthIndex,
                    value.Sales,
                    value.Invoices,
                    CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Month)));
            }
        }
        else
        {
            var days = end.DayNumber - start.DayNumber + 1;
            for (var dayIndex = 0; dayIndex < days; dayIndex++)
            {
                var day = start.AddDays(dayIndex);
                var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var value = byKey.GetValueOrDefault(key);
                points.Add(new DashboardSalesPoint(
                    dayIndex,
                    value.Sales,
                    value.Invoices,
                    period == "week"
                        ? day.ToString("dd MMM", CultureInfo.InvariantCulture)
                        : day.Day.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return points;
    }

    private static async Task<IReadOnlyList<DashboardTransactionItem>> ReadSaleTransactionsAsync(
        DbConnection connection,
        bool hasSaleLines,
        bool hasSalePayments,
        CancellationToken cancellationToken)
    {
        var itemExpression = hasSaleLines
            ? "(SELECT COUNT(*) FROM sale_lines sl WHERE sl.sale_id = s.id)"
            : "0";
        var paymentExpression = hasSalePayments
            ? """
              COALESCE(
                (
                  SELECT group_concat(method, ', ')
                  FROM (
                    SELECT DISTINCT sp2.method AS method
                    FROM sale_payments sp2
                    WHERE sp2.sale_id = s.id
                  )
                ),
                CASE WHEN CAST(s.due_total AS NUMERIC) > 0 THEN 'credit' ELSE '—' END
              )
              """
            : "CASE WHEN CAST(s.due_total AS NUMERIC) > 0 THEN 'credit' ELSE '—' END";

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                COALESCE(s.completed_at, s.created_at) AS occurred_at,
                s.sale_number,
                COALESCE(c.name, 'Walk-in Customer') AS party,
                {itemExpression} AS items,
                s.grand_total,
                {paymentExpression} AS payment_method,
                s.payment_status
            FROM sales s
            LEFT JOIN customers c ON c.id = s.customer_id
            WHERE s.status = 'completed'
            ORDER BY occurred_at DESC
            LIMIT 8;
            """;

        var result = new List<DashboardTransactionItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DashboardTransactionItem(
                ReadDateTimeOffset(reader, 0),
                "Sale",
                reader.IsDBNull(1) ? "—" : reader.GetString(1),
                reader.IsDBNull(2) ? "Walk-in Customer" : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                ReadDecimal(reader, 4),
                reader.IsDBNull(5) ? "—" : reader.GetString(5),
                reader.IsDBNull(6) ? "completed" : reader.GetString(6)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<DashboardTransactionItem>> ReadPurchaseTransactionsAsync(
        DbConnection connection,
        bool hasSuppliers,
        bool hasGoodsReceiptLines,
        CancellationToken cancellationToken)
    {
        var supplierExpression = hasSuppliers ? "COALESCE(su.name, 'Supplier')" : "'Supplier'";
        var supplierJoin = hasSuppliers
            ? "LEFT JOIN suppliers su ON su.id = pi.supplier_id"
            : string.Empty;
        var itemExpression = hasGoodsReceiptLines
            ? "(SELECT COUNT(*) FROM goods_receipt_lines grl WHERE grl.goods_receipt_id = pi.goods_receipt_id)"
            : "0";

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                pi.created_at,
                pi.invoice_number,
                {supplierExpression} AS party,
                {itemExpression} AS items,
                pi.grand_total,
                'Supplier invoice' AS payment_method,
                pi.status
            FROM purchase_invoices pi
            {supplierJoin}
            WHERE pi.status <> 'cancelled'
            ORDER BY pi.created_at DESC
            LIMIT 8;
            """;

        var result = new List<DashboardTransactionItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DashboardTransactionItem(
                ReadDateTimeOffset(reader, 0),
                "Purchase",
                reader.IsDBNull(1) ? "—" : reader.GetString(1),
                reader.IsDBNull(2) ? "Supplier" : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                ReadDecimal(reader, 4),
                reader.IsDBNull(5) ? "—" : reader.GetString(5),
                reader.IsDBNull(6) ? "open" : reader.GetString(6)));
        }

        return result;
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

    private static async Task<decimal?> ScalarNullableDecimalAsync(
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
        return value is null or DBNull ? null : ToDecimal(value);
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

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal)
    {
        var text = reader.IsDBNull(ordinal)
            ? null
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

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
