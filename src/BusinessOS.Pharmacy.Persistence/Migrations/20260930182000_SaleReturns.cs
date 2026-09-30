using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260930182000_SaleReturns")]
public partial class SaleReturns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sale_returns",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                return_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                sale_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                refund_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_returns", x => x.id);
                table.ForeignKey("FK_sale_returns_sales_sale_id", x => x.sale_id, "sales", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_sale_returns_stock_locations_stock_location_id", x => x.stock_location_id, "stock_locations", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sale_return_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_return_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_line_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                medicine_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                refund_amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                disposition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_return_lines", x => x.id);
                table.ForeignKey("FK_sale_return_lines_medicines_medicine_id", x => x.medicine_id, "medicines", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_sale_return_lines_sale_lines_sale_line_id", x => x.sale_line_id, "sale_lines", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_sale_return_lines_sale_returns_sale_return_id", x => x.sale_return_id, "sale_returns", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sale_return_refunds",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_return_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_return_refunds", x => x.id);
                table.ForeignKey("FK_sale_return_refunds_sale_returns_sale_return_id", x => x.sale_return_id, "sale_returns", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sale_return_allocations",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_return_line_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                sale_batch_allocation_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                product_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stock_movement_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                restocked = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_return_allocations", x => x.id);
                table.ForeignKey("FK_sale_return_allocations_product_batches_product_batch_id", x => x.product_batch_id, "product_batches", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_sale_return_allocations_sale_batch_allocations_sale_batch_allocation_id", x => x.sale_batch_allocation_id, "sale_batch_allocations", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_sale_return_allocations_sale_return_lines_sale_return_line_id", x => x.sale_return_line_id, "sale_return_lines", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_sale_return_allocations_stock_movements_stock_movement_id", x => x.stock_movement_id, "stock_movements", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_sale_returns_return_number", "sale_returns", "return_number", unique: true);
        migrationBuilder.CreateIndex("IX_sale_returns_business_date", "sale_returns", "business_date");
        migrationBuilder.CreateIndex("IX_sale_returns_status", "sale_returns", "status");
        migrationBuilder.CreateIndex("IX_sale_returns_idempotency_key", "sale_returns", "idempotency_key", unique: true);
        migrationBuilder.CreateIndex("IX_sale_returns_completed_at", "sale_returns", "completed_at");
        migrationBuilder.CreateIndex("IX_sale_returns_sale_id_status", "sale_returns", new[] { "sale_id", "status" });
        migrationBuilder.CreateIndex("IX_sale_returns_stock_location_id_business_date", "sale_returns", new[] { "stock_location_id", "business_date" });
        migrationBuilder.CreateIndex("IX_sale_return_lines_medicine_id", "sale_return_lines", "medicine_id");
        migrationBuilder.CreateIndex("IX_sale_return_lines_sale_line_id_sale_return_id", "sale_return_lines", new[] { "sale_line_id", "sale_return_id" });
        migrationBuilder.CreateIndex("IX_sale_return_lines_sale_return_id", "sale_return_lines", "sale_return_id");
        migrationBuilder.CreateIndex("IX_sale_return_refunds_method", "sale_return_refunds", "method");
        migrationBuilder.CreateIndex("IX_sale_return_refunds_sale_return_id_method", "sale_return_refunds", new[] { "sale_return_id", "method" });
        migrationBuilder.CreateIndex("IX_sale_return_allocations_product_batch_id", "sale_return_allocations", "product_batch_id");
        migrationBuilder.CreateIndex("IX_sale_return_allocations_stock_movement_id", "sale_return_allocations", "stock_movement_id");
        migrationBuilder.CreateIndex("sale_return_alloc_batch_line_idx", "sale_return_allocations", new[] { "sale_batch_allocation_id", "sale_return_line_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sale_return_allocations");
        migrationBuilder.DropTable("sale_return_refunds");
        migrationBuilder.DropTable("sale_return_lines");
        migrationBuilder.DropTable("sale_returns");
    }
}
