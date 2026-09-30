using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260930174000_PosSales")]
public partial class PosSales : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sales",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                customer_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                prescription_reference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                prescriber_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                prescription_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                subtotal = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                discount_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                tax_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                grand_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                paid_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                due_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                change_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                payment_status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                held_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales", x => x.id);
                table.ForeignKey(
                    name: "FK_sales_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_sales_stock_locations_stock_location_id",
                    column: x => x.stock_location_id,
                    principalTable: "stock_locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sale_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                medicine_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                description = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                sale_unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                unit_price = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                discount_amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                tax_amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                line_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                cost_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                prescription_required = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_sale_lines_medicines_medicine_id",
                    column: x => x.medicine_id,
                    principalTable: "medicines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sale_lines_sales_sale_id",
                    column: x => x.sale_id,
                    principalTable: "sales",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sale_payments",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                payment_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                paid_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_payments", x => x.id);
                table.ForeignKey(
                    name: "FK_sale_payments_sales_sale_id",
                    column: x => x.sale_id,
                    principalTable: "sales",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sale_batch_allocations",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_line_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                product_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stock_movement_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                unit_cost = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                unit_price = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                line_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_batch_allocations", x => x.id);
                table.ForeignKey(
                    name: "FK_sale_batch_allocations_product_batches_product_batch_id",
                    column: x => x.product_batch_id,
                    principalTable: "product_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sale_batch_allocations_sale_lines_sale_line_id",
                    column: x => x.sale_line_id,
                    principalTable: "sale_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_sale_batch_allocations_stock_movements_stock_movement_id",
                    column: x => x.stock_movement_id,
                    principalTable: "stock_movements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_sales_sale_number", "sales", "sale_number", unique: true);
        migrationBuilder.CreateIndex("IX_sales_stock_location_id", "sales", "stock_location_id");
        migrationBuilder.CreateIndex("IX_sales_customer_id", "sales", "customer_id");
        migrationBuilder.CreateIndex("IX_sales_prescription_reference", "sales", "prescription_reference");
        migrationBuilder.CreateIndex("IX_sales_business_date", "sales", "business_date");
        migrationBuilder.CreateIndex("IX_sales_status", "sales", "status");
        migrationBuilder.CreateIndex("IX_sales_payment_status", "sales", "payment_status");
        migrationBuilder.CreateIndex("IX_sales_idempotency_key", "sales", "idempotency_key", unique: true);
        migrationBuilder.CreateIndex("IX_sales_completed_at", "sales", "completed_at");
        migrationBuilder.CreateIndex("IX_sales_business_date_status", "sales", new[] { "business_date", "status" });
        migrationBuilder.CreateIndex("IX_sales_created_by_business_date", "sales", new[] { "created_by", "business_date" });

        migrationBuilder.CreateIndex("IX_sale_lines_medicine_id", "sale_lines", "medicine_id");
        migrationBuilder.CreateIndex("IX_sale_lines_sale_id_medicine_id", "sale_lines", new[] { "sale_id", "medicine_id" });

        migrationBuilder.CreateIndex("IX_sale_payments_payment_number", "sale_payments", "payment_number", unique: true);
        migrationBuilder.CreateIndex("IX_sale_payments_method", "sale_payments", "method");
        migrationBuilder.CreateIndex("IX_sale_payments_paid_at", "sale_payments", "paid_at");
        migrationBuilder.CreateIndex("IX_sale_payments_sale_id_method", "sale_payments", new[] { "sale_id", "method" });

        migrationBuilder.CreateIndex("IX_sale_batch_allocations_product_batch_id", "sale_batch_allocations", "product_batch_id");
        migrationBuilder.CreateIndex("IX_sale_batch_allocations_stock_movement_id", "sale_batch_allocations", "stock_movement_id", unique: true);
        migrationBuilder.CreateIndex("IX_sale_batch_allocations_sale_line_id_product_batch_id", "sale_batch_allocations", new[] { "sale_line_id", "product_batch_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sale_batch_allocations");
        migrationBuilder.DropTable("sale_payments");
        migrationBuilder.DropTable("sale_lines");
        migrationBuilder.DropTable("sales");
    }
}
