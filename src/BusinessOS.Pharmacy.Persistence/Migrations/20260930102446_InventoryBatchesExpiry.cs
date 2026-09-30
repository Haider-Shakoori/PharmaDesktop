using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260930102446_InventoryBatchesExpiry")]
public partial class InventoryBatchesExpiry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "branches",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                code = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_branches", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "stock_locations",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                branch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                code = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_locations", x => x.id);
                table.ForeignKey(
                    name: "FK_stock_locations_branches_branch_id",
                    column: x => x.branch_id,
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inventory_adjustments",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                reason_code = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                posted_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                posted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory_adjustments", x => x.id);
                table.ForeignKey(
                    name: "FK_inventory_adjustments_stock_locations_stock_location_id",
                    column: x => x.stock_location_id,
                    principalTable: "stock_locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "product_batches",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                medicine_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                supplier_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                purchase_order_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                goods_receipt_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                branch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                batch_number = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                batch_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                manufactured_at = table.Column<DateOnly>(type: "TEXT", nullable: true),
                expires_at = table.Column<DateOnly>(type: "TEXT", nullable: true),
                status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                received_quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                available_quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                purchase_cost = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                sale_price = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                last_movement_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_batches", x => x.id);
                table.ForeignKey(
                    name: "FK_product_batches_branches_branch_id",
                    column: x => x.branch_id,
                    principalTable: "branches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_product_batches_medicines_medicine_id",
                    column: x => x.medicine_id,
                    principalTable: "medicines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_product_batches_stock_locations_stock_location_id",
                    column: x => x.stock_location_id,
                    principalTable: "stock_locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "batch_status_events",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                product_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                from_status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                to_status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                actor_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                changed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_batch_status_events", x => x.id);
                table.ForeignKey(
                    name: "FK_batch_status_events_product_batches_product_batch_id",
                    column: x => x.product_batch_id,
                    principalTable: "product_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inventory_adjustment_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                inventory_adjustment_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                product_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                quantity_delta = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory_adjustment_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_inventory_adjustment_lines_inventory_adjustments_inventory_adjustment_id",
                    column: x => x.inventory_adjustment_id,
                    principalTable: "inventory_adjustments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_inventory_adjustment_lines_product_batches_product_batch_id",
                    column: x => x.product_batch_id,
                    principalTable: "product_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_movements",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                product_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                medicine_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                branch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                movement_type = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                quantity_delta = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                balance_after = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                unit_cost = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                source_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                source_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                source_line_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                reason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                actor_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                metadata = table.Column<string>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_movements", x => x.id);
                table.ForeignKey(
                    name: "FK_stock_movements_product_batches_product_batch_id",
                    column: x => x.product_batch_id,
                    principalTable: "product_batches",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_branches_code", "branches", "code", unique: true);
        migrationBuilder.CreateIndex("IX_branches_is_active", "branches", "is_active");
        migrationBuilder.CreateIndex("IX_branches_is_default", "branches", "is_default");

        migrationBuilder.CreateIndex("IX_stock_locations_is_default", "stock_locations", "is_default");
        migrationBuilder.CreateIndex(
            "IX_stock_locations_branch_id_code",
            "stock_locations",
            new[] { "branch_id", "code" },
            unique: true);
        migrationBuilder.CreateIndex(
            "IX_stock_locations_branch_id_is_active",
            "stock_locations",
            new[] { "branch_id", "is_active" });

        migrationBuilder.CreateIndex("IX_inventory_adjustments_number", "inventory_adjustments", "number", unique: true);
        migrationBuilder.CreateIndex("IX_inventory_adjustments_posted_at", "inventory_adjustments", "posted_at");
        migrationBuilder.CreateIndex("IX_inventory_adjustments_reason_code", "inventory_adjustments", "reason_code");
        migrationBuilder.CreateIndex("IX_inventory_adjustments_status", "inventory_adjustments", "status");
        migrationBuilder.CreateIndex("IX_inventory_adjustments_stock_location_id", "inventory_adjustments", "stock_location_id");

        migrationBuilder.CreateIndex("IX_product_batches_batch_number", "product_batches", "batch_number");
        migrationBuilder.CreateIndex("IX_product_batches_branch_id", "product_batches", "branch_id");
        migrationBuilder.CreateIndex("IX_product_batches_expires_at", "product_batches", "expires_at");
        migrationBuilder.CreateIndex("IX_product_batches_last_movement_at", "product_batches", "last_movement_at");
        migrationBuilder.CreateIndex("IX_product_batches_status", "product_batches", "status");
        migrationBuilder.CreateIndex(
            "IX_product_batches_medicine_id_status_expires_at",
            "product_batches",
            new[] { "medicine_id", "status", "expires_at" });
        migrationBuilder.CreateIndex(
            "IX_product_batches_medicine_id_stock_location_id_batch_key",
            "product_batches",
            new[] { "medicine_id", "stock_location_id", "batch_key" },
            unique: true);
        migrationBuilder.CreateIndex(
            "IX_product_batches_stock_location_id_status_expires_at",
            "product_batches",
            new[] { "stock_location_id", "status", "expires_at" });

        migrationBuilder.CreateIndex("IX_batch_status_events_changed_at", "batch_status_events", "changed_at");
        migrationBuilder.CreateIndex("IX_batch_status_events_product_batch_id", "batch_status_events", "product_batch_id");

        migrationBuilder.CreateIndex(
            "IX_inventory_adjustment_lines_inventory_adjustment_id_product_batch_id",
            "inventory_adjustment_lines",
            new[] { "inventory_adjustment_id", "product_batch_id" });
        migrationBuilder.CreateIndex(
            "IX_inventory_adjustment_lines_product_batch_id",
            "inventory_adjustment_lines",
            "product_batch_id");

        migrationBuilder.CreateIndex("IX_stock_movements_idempotency_key", "stock_movements", "idempotency_key", unique: true);
        migrationBuilder.CreateIndex(
            "IX_stock_movements_medicine_id_occurred_at",
            "stock_movements",
            new[] { "medicine_id", "occurred_at" });
        migrationBuilder.CreateIndex("IX_stock_movements_movement_type", "stock_movements", "movement_type");
        migrationBuilder.CreateIndex("IX_stock_movements_occurred_at", "stock_movements", "occurred_at");
        migrationBuilder.CreateIndex(
            "IX_stock_movements_product_batch_id_occurred_at",
            "stock_movements",
            new[] { "product_batch_id", "occurred_at" });
        migrationBuilder.CreateIndex(
            "IX_stock_movements_source_type_source_id",
            "stock_movements",
            new[] { "source_type", "source_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("batch_status_events");
        migrationBuilder.DropTable("inventory_adjustment_lines");
        migrationBuilder.DropTable("stock_movements");
        migrationBuilder.DropTable("inventory_adjustments");
        migrationBuilder.DropTable("product_batches");
        migrationBuilder.DropTable("stock_locations");
        migrationBuilder.DropTable("branches");
    }
}
