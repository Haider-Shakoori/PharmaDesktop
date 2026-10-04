using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class PurchasingTests
{
    [Fact]
    public async Task Supplier_create_update_search_and_duplicate_code_are_enforced()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var suppliers = provider.GetRequiredService<ISupplierService>();

            var id = await suppliers.CreateAsync(
                new SaveSupplierRequest(
                    "SUP-001",
                    "Kabul Medical Supply",
                    "Ahmad",
                    "0700000000",
                    "0700000000",
                    "supplier@example.test",
                    "Shahr-e-Naw",
                    "Kabul",
                    "Kabul",
                    30,
                    1250.50m,
                    true,
                    "Primary wholesaler"));

            var created = await suppliers.GetAsync(id);
            Assert.NotNull(created);
            Assert.Equal("SUP-001", created!.Code);
            Assert.Equal("Kabul Medical Supply", created.Name);
            Assert.Equal(30, created.PaymentTermsDays);
            Assert.Equal(1250.50m, created.OpeningBalance);

            await suppliers.UpdateAsync(
                id,
                new SaveSupplierRequest(
                    "SUP-001",
                    "Kabul Medical Supply Ltd",
                    "Ahmad",
                    "0700000001",
                    "0700000000",
                    "supplier@example.test",
                    "Shahr-e-Naw",
                    "Kabul",
                    "Kabul",
                    45,
                    9999m,
                    true,
                    "Updated terms"));

            var search = await suppliers.SearchAsync(
                new SupplierSearchFilter("Medical Supply", IsActive: true));

            var updated = Assert.Single(search);
            Assert.Equal(id, updated.Id);
            Assert.Equal("Kabul Medical Supply Ltd", updated.Name);
            Assert.Equal(45, updated.PaymentTermsDays);
            Assert.Equal(1250.50m, updated.OpeningBalance);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                suppliers.CreateAsync(
                    new SaveSupplierRequest(
                        "SUP-001",
                        "Duplicate Supplier",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        0,
                        0m,
                        true,
                        null)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Supplier_summary_combines_deals_payments_and_opening_balances()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var suppliers = provider.GetRequiredService<ISupplierService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();

            var supplierId = await suppliers.CreateAsync(
                new SaveSupplierRequest(
                    "SUP-SUMMARY",
                    "Summary Supplier",
                    null,
                    "0700000099",
                    null,
                    null,
                    null,
                    "Kabul",
                    "Kabul",
                    30,
                    200m,
                    true,
                    null));

            var medicineId = await CreateMedicineAsync(provider, "SUP-SUM-MED", "Summary Medicine");
            var orderId = await purchasing.CreateOrderAsync(
                new CreatePurchaseOrderRequest(
                    supplierId,
                    new DateOnly(2026, 10, 4),
                    null,
                    "AFN",
                    null,
                    [new CreatePurchaseOrderLineRequest(medicineId, 10m, 50m)]));

            await purchasing.SubmitOrderAsync(orderId);
            await purchasing.ApproveOrderAsync(orderId);

            var invoiceId = await purchasing.CreateInvoiceAsync(
                orderId,
                new CreatePurchaseInvoiceRequest(
                    "SUP-SUM-INV",
                    null,
                    new DateOnly(2026, 10, 4),
                    null,
                    null));

            await purchasing.RecordSupplierPaymentAsync(
                invoiceId,
                new RecordSupplierPaymentRequest(
                    125m,
                    "AFN",
                    "cash",
                    null,
                    DateTimeOffset.Parse("2026-10-04T09:00:00+04:30"),
                    "supplier-summary-payment",
                    null));

            var summary = await suppliers.GetSummaryAsync();

            Assert.Equal(1, summary.TotalSuppliers);
            Assert.Equal(500m, summary.TotalDealValue);
            Assert.Equal(125m, summary.TotalPaid);
            Assert.Equal(575m, summary.OutstandingPayable);
            Assert.Equal(200m, summary.TotalOpeningBalance);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Purchase_order_totals_and_approval_lifecycle_match_web_rules()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var supplierId = await CreateSupplierAsync(provider, paymentTermsDays: 15);
            var medicineId = await CreateMedicineAsync(provider, "PO-001", "Amoxicillin");

            var orderId = await purchasing.CreateOrderAsync(
                new CreatePurchaseOrderRequest(
                    supplierId,
                    new DateOnly(2026, 9, 30),
                    new DateOnly(2026, 10, 5),
                    "afn",
                    "Initial pharmacy order",
                    [
                        new CreatePurchaseOrderLineRequest(
                            medicineId,
                            10m,
                            20m,
                            DiscountAmount: 10m,
                            LandedCostAllocated: 5m)
                    ]));

            var draft = await purchasing.GetOrderAsync(orderId);
            Assert.NotNull(draft);
            Assert.Equal("draft", draft!.Order.Status);
            Assert.Equal("AFN", draft.Order.Currency);
            Assert.Equal(200m, draft.Order.Subtotal);
            Assert.Equal(10m, draft.Order.DiscountTotal);
            Assert.Equal(5m, draft.Order.LandedCostTotal);
            Assert.Equal(195m, draft.Order.GrandTotal);

            var line = Assert.Single(draft.Lines);
            Assert.Equal(10m, line.OrderedQuantity);
            Assert.Equal(0m, line.ReceivedQuantity);
            Assert.Equal(195m, line.LineTotal);

            await purchasing.SubmitOrderAsync(orderId);
            Assert.Equal("submitted", (await purchasing.GetOrderAsync(orderId))!.Order.Status);

            await purchasing.ApproveOrderAsync(orderId);
            var approved = await purchasing.GetOrderAsync(orderId);
            Assert.NotNull(approved);
            Assert.Equal("approved", approved!.Order.Status);
            Assert.NotNull(approved.ApprovedAt);
            Assert.Equal("user-purchasing", approved.ApprovedBy);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purchasing.SubmitOrderAsync(orderId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Partial_receipts_post_bonus_stock_and_weighted_cost_then_complete_order()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var inventory = provider.GetRequiredService<IInventoryService>();

            await inventory.EnsureDefaultsAsync();
            var location = (await inventory.GetReferenceDataAsync()).Locations.Single();

            var supplierId = await CreateSupplierAsync(provider, paymentTermsDays: 30);
            var medicineId = await CreateMedicineAsync(provider, "GRN-001", "Cefixime");

            var orderId = await purchasing.CreateOrderAsync(
                new CreatePurchaseOrderRequest(
                    supplierId,
                    new DateOnly(2026, 9, 30),
                    null,
                    "AFN",
                    null,
                    [
                        new CreatePurchaseOrderLineRequest(
                            medicineId,
                            10m,
                            20m,
                            DiscountAmount: 10m,
                            LandedCostAllocated: 20m)
                    ]));

            await purchasing.SubmitOrderAsync(orderId);
            await purchasing.ApproveOrderAsync(orderId);

            var orderLine = Assert.Single((await purchasing.GetOrderAsync(orderId))!.Lines);

            var firstReceiptId = await purchasing.CaptureGoodsReceiptAsync(
                orderId,
                new CaptureGoodsReceiptRequest(
                    DateTimeOffset.Parse("2026-09-30T12:00:00+04:30"),
                    "grn-test-1",
                    "First delivery",
                    [
                        new CaptureGoodsReceiptLineRequest(
                            orderLine.Id,
                            5m,
                            1m,
                            "LOT-100",
                            new DateOnly(2026, 9, 1),
                            new DateOnly(2027, 9, 1),
                            22m,
                            30m)
                    ]));

            await purchasing.PostGoodsReceiptAsync(firstReceiptId, location.Id);

            var afterFirst = await purchasing.GetOrderAsync(orderId);
            Assert.NotNull(afterFirst);
            Assert.Equal("partially_received", afterFirst!.Order.Status);
            Assert.Equal(5m, Assert.Single(afterFirst.Lines).ReceivedQuantity);

            var firstReceipt = Assert.Single(afterFirst.Receipts);
            Assert.Equal("posted", firstReceipt.Status);
            Assert.NotNull(firstReceipt.InventoryPostedAt);

            var firstBatch = Assert.Single(
                await inventory.SearchBatchesAsync(
                    new InventoryBatchFilter(Search: "LOT-100", Take: 20)));

            Assert.Equal(6m, firstBatch.ReceivedQuantity);
            Assert.Equal(6m, firstBatch.AvailableQuantity);
            Assert.Equal(19.1667m, firstBatch.PurchaseCost);
            Assert.Equal(30m, firstBatch.SalePrice);

            var firstDetail = await inventory.GetBatchAsync(firstBatch.Id);
            Assert.NotNull(firstDetail);
            var firstMovement = Assert.Single(firstDetail!.Movements);
            Assert.Equal("purchase_receipt", firstMovement.MovementType);
            Assert.Equal(6m, firstMovement.QuantityDelta);
            Assert.Equal(19.1667m, firstMovement.UnitCost);

            var refreshedLine = Assert.Single(afterFirst.Lines);
            var secondReceiptId = await purchasing.CaptureGoodsReceiptAsync(
                orderId,
                new CaptureGoodsReceiptRequest(
                    DateTimeOffset.Parse("2026-10-01T09:00:00+04:30"),
                    "grn-test-2",
                    "Final delivery",
                    [
                        new CaptureGoodsReceiptLineRequest(
                            refreshedLine.Id,
                            5m,
                            0m,
                            "LOT-100",
                            new DateOnly(2026, 9, 1),
                            new DateOnly(2027, 9, 1),
                            20m,
                            31m)
                    ]));

            await purchasing.PostGoodsReceiptAsync(secondReceiptId, location.Id);

            var completed = await purchasing.GetOrderAsync(orderId);
            Assert.NotNull(completed);
            Assert.Equal("received", completed!.Order.Status);
            Assert.Equal(10m, Assert.Single(completed.Lines).ReceivedQuantity);
            Assert.Equal(2, completed.Receipts.Count);

            var finalBatch = Assert.Single(
                await inventory.SearchBatchesAsync(
                    new InventoryBatchFilter(Search: "LOT-100", Take: 20)));

            Assert.Equal(11m, finalBatch.ReceivedQuantity);
            Assert.Equal(11m, finalBatch.AvailableQuantity);
            Assert.Equal(20m, finalBatch.PurchaseCost);
            Assert.Equal(31m, finalBatch.SalePrice);

            var finalDetail = await inventory.GetBatchAsync(finalBatch.Id);
            Assert.NotNull(finalDetail);
            Assert.Equal(2, finalDetail!.Movements.Count(x => x.MovementType == "purchase_receipt"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Pending_receipts_count_against_ordered_quantity_before_inventory_posting()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var supplierId = await CreateSupplierAsync(provider, paymentTermsDays: 0);
            var medicineId = await CreateMedicineAsync(provider, "PEND-001", "Pending Receipt Medicine");

            var orderId = await purchasing.CreateOrderAsync(
                new CreatePurchaseOrderRequest(
                    supplierId,
                    new DateOnly(2026, 9, 30),
                    null,
                    "AFN",
                    null,
                    [new CreatePurchaseOrderLineRequest(medicineId, 10m, 10m)]));

            await purchasing.SubmitOrderAsync(orderId);
            await purchasing.ApproveOrderAsync(orderId);

            var line = Assert.Single((await purchasing.GetOrderAsync(orderId))!.Lines);

            await purchasing.CaptureGoodsReceiptAsync(
                orderId,
                new CaptureGoodsReceiptRequest(
                    DateTimeOffset.UtcNow,
                    "pending-1",
                    null,
                    [
                        new CaptureGoodsReceiptLineRequest(
                            line.Id,
                            6m,
                            0m,
                            "PENDING-LOT",
                            null,
                            new DateOnly(2027, 1, 1),
                            10m,
                            15m)
                    ]));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purchasing.CaptureGoodsReceiptAsync(
                    orderId,
                    new CaptureGoodsReceiptRequest(
                        DateTimeOffset.UtcNow,
                        "pending-2",
                        null,
                        [
                            new CaptureGoodsReceiptLineRequest(
                                line.Id,
                                5m,
                                0m,
                                "PENDING-LOT-2",
                                null,
                                new DateOnly(2027, 2, 1),
                                10m,
                                15m)
                        ])));

            var order = await purchasing.GetOrderAsync(orderId);
            Assert.NotNull(order);
            var receipt = Assert.Single(order!.Receipts);
            Assert.Equal("pending_inventory", receipt.Status);
            Assert.Null(receipt.InventoryPostedAt);
            Assert.Equal(0m, Assert.Single(order.Lines).ReceivedQuantity);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Supplier_invoice_payments_are_idempotent_capped_and_update_balance()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var supplierId = await CreateSupplierAsync(provider, paymentTermsDays: 20);
            var medicineId = await CreateMedicineAsync(provider, "INV-PO-001", "Invoice Medicine");

            var orderId = await purchasing.CreateOrderAsync(
                new CreatePurchaseOrderRequest(
                    supplierId,
                    new DateOnly(2026, 9, 30),
                    null,
                    "AFN",
                    null,
                    [new CreatePurchaseOrderLineRequest(medicineId, 10m, 10m)]));

            await purchasing.SubmitOrderAsync(orderId);
            await purchasing.ApproveOrderAsync(orderId);

            var invoiceId = await purchasing.CreateInvoiceAsync(
                orderId,
                new CreatePurchaseInvoiceRequest(
                    "SUP-INV-7788",
                    null,
                    new DateOnly(2026, 9, 30),
                    null,
                    "Supplier invoice"));

            var initialInvoice = Assert.Single((await purchasing.GetOrderAsync(orderId))!.Invoices);
            Assert.Equal(invoiceId, initialInvoice.Id);
            Assert.Equal(new DateOnly(2026, 10, 20), initialInvoice.DueDate);
            Assert.Equal(100m, initialInvoice.GrandTotal);
            Assert.Equal(100m, initialInvoice.BalanceDue);
            Assert.Equal("open", initialInvoice.Status);

            var firstPaymentId = await purchasing.RecordSupplierPaymentAsync(
                invoiceId,
                new RecordSupplierPaymentRequest(
                    40m,
                    "afn",
                    "cash",
                    "CASH-001",
                    DateTimeOffset.Parse("2026-09-30T15:00:00+04:30"),
                    "payment-idem-1",
                    "First payment"));

            var replayPaymentId = await purchasing.RecordSupplierPaymentAsync(
                invoiceId,
                new RecordSupplierPaymentRequest(
                    40m,
                    "AFN",
                    "cash",
                    "CASH-001",
                    DateTimeOffset.Parse("2026-09-30T15:00:00+04:30"),
                    "payment-idem-1",
                    "Replay"));

            Assert.Equal(firstPaymentId, replayPaymentId);

            var partial = Assert.Single((await purchasing.GetOrderAsync(orderId))!.Invoices);
            Assert.Equal("partially_paid", partial.Status);
            Assert.Equal(40m, partial.PaidTotal);
            Assert.Equal(60m, partial.BalanceDue);
            Assert.Single(partial.Payments);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                purchasing.RecordSupplierPaymentAsync(
                    invoiceId,
                    new RecordSupplierPaymentRequest(
                        61m,
                        "AFN",
                        "bank",
                        null,
                        DateTimeOffset.UtcNow,
                        "payment-too-large",
                        null)));

            await purchasing.RecordSupplierPaymentAsync(
                invoiceId,
                new RecordSupplierPaymentRequest(
                    60m,
                    "AFN",
                    "hawala",
                    "HAWALA-001",
                    DateTimeOffset.Parse("2026-10-01T10:00:00+04:30"),
                    "payment-idem-2",
                    "Final payment"));

            var paid = Assert.Single((await purchasing.GetOrderAsync(orderId))!.Invoices);
            Assert.Equal("paid", paid.Status);
            Assert.Equal(100m, paid.PaidTotal);
            Assert.Equal(0m, paid.BalanceDue);
            Assert.Equal(2, paid.Payments.Count);
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
            .InitializeAsync("tenant-purchasing");
    }

    private static async Task<string> CreateSupplierAsync(
        ServiceProvider provider,
        int paymentTermsDays)
    {
        var suppliers = provider.GetRequiredService<ISupplierService>();
        return await suppliers.CreateAsync(
            new SaveSupplierRequest(
                "SUP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                "Test Supplier",
                null,
                "0700000000",
                null,
                null,
                null,
                "Kabul",
                "Kabul",
                paymentTermsDays,
                0m,
                true,
                null));
    }

    private static async Task<string> CreateMedicineAsync(
        ServiceProvider provider,
        string code,
        string name)
    {
        var catalog = provider.GetRequiredService<IMedicineCatalogService>();
        return await catalog.CreateAsync(
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
            "darmaltoon-purchasing-tests",
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
            "user-purchasing",
            "tenant-purchasing",
            "activation-purchasing",
            "device-purchasing",
            "Purchasing Tester",
            "purchasing@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "purchasing" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "medicines.manage",
                "inventory.manage",
                "inventory.adjust",
                "inventory.status",
                "purchases.manage",
                "purchases.approve",
                "purchases.pay",
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
