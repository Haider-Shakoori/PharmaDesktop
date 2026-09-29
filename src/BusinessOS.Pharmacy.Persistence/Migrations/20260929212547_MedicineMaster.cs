using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MedicineMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manufacturers",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false, collation: "NOCASE"),
                    country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "medicine_categories",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false, collation: "NOCASE"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicine_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "medicines",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    medicine_category_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    manufacturer_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    medicine_code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    barcode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true, collation: "NOCASE"),
                    brand_name = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false, collation: "NOCASE"),
                    generic_name = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true, collation: "NOCASE"),
                    strength = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    dosage_form = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    purchase_unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    sale_unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    units_per_purchase_unit = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: false),
                    reorder_level = table.Column<decimal>(type: "TEXT", precision: 14, scale: 4, nullable: false),
                    prescription_required = table.Column<bool>(type: "INTEGER", nullable: false),
                    batch_tracking_required = table.Column<bool>(type: "INTEGER", nullable: false),
                    expiry_tracking_required = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicines", x => x.id);
                    table.ForeignKey(
                        name: "FK_medicines_manufacturers_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalTable: "manufacturers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_medicines_medicine_categories_medicine_category_id",
                        column: x => x.medicine_category_id,
                        principalTable: "medicine_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_is_active",
                table: "manufacturers",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_name",
                table: "manufacturers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicine_categories_is_active",
                table: "medicine_categories",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_medicine_categories_name",
                table: "medicine_categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_barcode",
                table: "medicines",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_brand_name",
                table: "medicines",
                column: "brand_name");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_generic_name",
                table: "medicines",
                column: "generic_name");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_is_active",
                table: "medicines",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_manufacturer_id",
                table: "medicines",
                column: "manufacturer_id");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_medicine_category_id_is_active",
                table: "medicines",
                columns: new[] { "medicine_category_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_medicines_medicine_code",
                table: "medicines",
                column: "medicine_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medicines");

            migrationBuilder.DropTable(
                name: "manufacturers");

            migrationBuilder.DropTable(
                name: "medicine_categories");
        }
    }
}
