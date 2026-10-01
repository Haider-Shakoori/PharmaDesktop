using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260930170000_SuppliersPurchases")]
public partial class SuppliersPurchases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "suppliers",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                code = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                name = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                contact_person = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                phone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                whatsapp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                address = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                city = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                province = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                payment_terms_days = table.Column<int>(type: "INTEGER", nullable: false),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_suppliers", x => x.id));

        migrationBuilder.CreateTable(
            name: "purchase_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                supplier_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                order_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                expected_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                subtotal = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                discount_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                landed_cost_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                grand_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                approved_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                submitted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                cancelled_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_orders", x => x.id);
                table.ForeignKey(
                    name: "FK_purchase_orders_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_order_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                purchase_order_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                medicine_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                description = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                ordered_quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                received_quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                unit_cost = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                discount_amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                landed_cost_allocated = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                line_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_order_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_purchase_order_lines_medicines_medicine_id",
                    column: x => x.medicine_id,
                    principalTable: "medicines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_purchase_order_lines_purchase_orders_purchase_order_id",
                    column: x => x.purchase_order_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipts",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                purchase_order_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                supplier_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                receipt_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                inventory_posted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_goods_receipts", x => x.id);
                table.ForeignKey(
                    name: "FK_goods_receipts_purchase_orders_purchase_order_id",
                    column: x => x.purchase_order_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_goods_receipts_stock_locations_stock_location_id",
                    column: x => x.stock_location_id,
                    principalTable: "stock_locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_goods_receipts_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipt_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                goods_receipt_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                purchase_order_line_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                medicine_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                received_quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                bonus_quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                batch_number = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                manufactured_at = table.Column<DateOnly>(type: "TEXT", nullable: true),
                expires_at = table.Column<DateOnly>(type: "TEXT", nullable: true),
                unit_cost = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                sale_price = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_goods_receipt_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_goods_receipt_lines_goods_receipts_goods_receipt_id",
                    column: x => x.goods_receipt_id,
                    principalTable: "goods_receipts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_goods_receipt_lines_medicines_medicine_id",
                    column: x => x.medicine_id,
                    principalTable: "medicines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_goods_receipt_lines_purchase_order_lines_purchase_order_line_id",
                    column: x => x.purchase_order_line_id,
                    principalTable: "purchase_order_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_invoices",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                supplier_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                purchase_order_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                goods_receipt_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                invoice_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                supplier_invoice_number = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                invoice_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                due_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                subtotal = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                discount_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                landed_cost_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                grand_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                paid_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                balance_due = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_invoices", x => x.id);
                table.ForeignKey(
                    name: "FK_purchase_invoices_goods_receipts_goods_receipt_id",
                    column: x => x.goods_receipt_id,
                    principalTable: "goods_receipts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_purchase_invoices_purchase_orders_purchase_order_id",
                    column: x => x.purchase_order_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_purchase_invoices_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_payments",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                purchase_invoice_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                supplier_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                payment_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                paid_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_payments", x => x.id);
                table.ForeignKey(
                    name: "FK_supplier_payments_purchase_invoices_purchase_invoice_id",
                    column: x => x.purchase_invoice_id,
                    principalTable: "purchase_invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_payments_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_suppliers_code", "suppliers", "code", unique: true);
        migrationBuilder.CreateIndex("IX_suppliers_name", "suppliers", "name");
        migrationBuilder.CreateIndex("IX_suppliers_is_active", "suppliers", "is_active");

        migrationBuilder.CreateIndex("IX_purchase_orders_number", "purchase_orders", "number", unique: true);
        migrationBuilder.CreateIndex("IX_purchase_orders_status", "purchase_orders", "status");
        migrationBuilder.CreateIndex("IX_purchase_orders_order_date", "purchase_orders", "order_date");
        migrationBuilder.CreateIndex("IX_purchase_orders_expected_date", "purchase_orders", "expected_date");
        migrationBuilder.CreateIndex("IX_purchase_orders_supplier_id_order_date", "purchase_orders", new[] { "supplier_id", "order_date" });

        migrationBuilder.CreateIndex("IX_purchase_order_lines_medicine_id", "purchase_order_lines", "medicine_id");
        migrationBuilder.CreateIndex("IX_purchase_order_lines_purchase_order_id_medicine_id", "purchase_order_lines", new[] { "purchase_order_id", "medicine_id" });

        migrationBuilder.CreateIndex("IX_goods_receipts_purchase_order_id", "goods_receipts", "purchase_order_id");
        migrationBuilder.CreateIndex("IX_goods_receipts_supplier_id", "goods_receipts", "supplier_id");
        migrationBuilder.CreateIndex("IX_goods_receipts_stock_location_id", "goods_receipts", "stock_location_id");
        migrationBuilder.CreateIndex("IX_goods_receipts_receipt_number", "goods_receipts", "receipt_number", unique: true);
        migrationBuilder.CreateIndex("IX_goods_receipts_status", "goods_receipts", "status");
        migrationBuilder.CreateIndex("IX_goods_receipts_received_at", "goods_receipts", "received_at");
        migrationBuilder.CreateIndex("IX_goods_receipts_inventory_posted_at", "goods_receipts", "inventory_posted_at");
        migrationBuilder.CreateIndex("IX_goods_receipts_idempotency_key", "goods_receipts", "idempotency_key", unique: true);

        migrationBuilder.CreateIndex("IX_goods_receipt_lines_goods_receipt_id", "goods_receipt_lines", "goods_receipt_id");
        migrationBuilder.CreateIndex("IX_goods_receipt_lines_purchase_order_line_id", "goods_receipt_lines", "purchase_order_line_id");
        migrationBuilder.CreateIndex("IX_goods_receipt_lines_batch_number", "goods_receipt_lines", "batch_number");
        migrationBuilder.CreateIndex("IX_goods_receipt_lines_expires_at", "goods_receipt_lines", "expires_at");
        migrationBuilder.CreateIndex("IX_goods_receipt_lines_medicine_id_expires_at", "goods_receipt_lines", new[] { "medicine_id", "expires_at" });

        migrationBuilder.CreateIndex("IX_purchase_invoices_supplier_id", "purchase_invoices", "supplier_id");
        migrationBuilder.CreateIndex("IX_purchase_invoices_purchase_order_id", "purchase_invoices", "purchase_order_id");
        migrationBuilder.CreateIndex("IX_purchase_invoices_goods_receipt_id", "purchase_invoices", "goods_receipt_id");
        migrationBuilder.CreateIndex("IX_purchase_invoices_invoice_number", "purchase_invoices", "invoice_number", unique: true);
        migrationBuilder.CreateIndex("IX_purchase_invoices_invoice_date", "purchase_invoices", "invoice_date");
        migrationBuilder.CreateIndex("IX_purchase_invoices_due_date", "purchase_invoices", "due_date");
        migrationBuilder.CreateIndex("IX_purchase_invoices_status", "purchase_invoices", "status");
        migrationBuilder.CreateIndex(
            name: "IX_purchase_invoices_supplier_id_supplier_invoice_number",
            table: "purchase_invoices",
            columns: new[] { "supplier_id", "supplier_invoice_number" },
            unique: true);
        migrationBuilder.CreateIndex("IX_purchase_invoices_supplier_id_invoice_date", "purchase_invoices", new[] { "supplier_id", "invoice_date" });

        migrationBuilder.CreateIndex("IX_supplier_payments_purchase_invoice_id", "supplier_payments", "purchase_invoice_id");
        migrationBuilder.CreateIndex("IX_supplier_payments_supplier_id", "supplier_payments", "supplier_id");
        migrationBuilder.CreateIndex("IX_supplier_payments_payment_number", "supplier_payments", "payment_number", unique: true);
        migrationBuilder.CreateIndex("IX_supplier_payments_paid_at", "supplier_payments", "paid_at");
        migrationBuilder.CreateIndex("IX_supplier_payments_idempotency_key", "supplier_payments", "idempotency_key", unique: true);
        migrationBuilder.CreateIndex("IX_supplier_payments_supplier_id_paid_at", "supplier_payments", new[] { "supplier_id", "paid_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("supplier_payments");
        migrationBuilder.DropTable("purchase_invoices");
        migrationBuilder.DropTable("goods_receipt_lines");
        migrationBuilder.DropTable("goods_receipts");
        migrationBuilder.DropTable("purchase_order_lines");
        migrationBuilder.DropTable("purchase_orders");
        migrationBuilder.DropTable("suppliers");
    }
}
