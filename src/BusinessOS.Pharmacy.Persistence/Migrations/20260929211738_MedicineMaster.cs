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
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false, collation: "NOCASE"),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medicine_categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicine_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medicines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    MedicineCategoryId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    ManufacturerId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    MedicineCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true, collation: "NOCASE"),
                    BrandName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false, collation: "NOCASE"),
                    GenericName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true, collation: "NOCASE"),
                    Strength = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DosageForm = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    PurchaseUnit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SaleUnit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UnitsPerPurchaseUnit = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: false),
                    ReorderLevel = table.Column<decimal>(type: "TEXT", precision: 14, scale: 4, nullable: false),
                    PrescriptionRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    BatchTrackingRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiryTrackingRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_medicines_manufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "manufacturers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_medicines_medicine_categories_MedicineCategoryId",
                        column: x => x.MedicineCategoryId,
                        principalTable: "medicine_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_IsActive",
                table: "manufacturers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_Name",
                table: "manufacturers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicine_categories_IsActive",
                table: "medicine_categories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_medicine_categories_Name",
                table: "medicine_categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_Barcode",
                table: "medicines",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_BrandName",
                table: "medicines",
                column: "BrandName");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_GenericName",
                table: "medicines",
                column: "GenericName");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_IsActive",
                table: "medicines",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_ManufacturerId",
                table: "medicines",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_medicines_MedicineCategoryId_IsActive",
                table: "medicines",
                columns: new[] { "MedicineCategoryId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_medicines_MedicineCode",
                table: "medicines",
                column: "MedicineCode",
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
