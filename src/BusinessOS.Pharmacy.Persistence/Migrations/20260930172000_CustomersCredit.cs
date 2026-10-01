using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260930172000_CustomersCredit")]
public partial class CustomersCredit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customers",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                name = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                phone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                credit_limit = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_customers", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_customers_phone",
            table: "customers",
            column: "phone");

        migrationBuilder.CreateIndex(
            name: "IX_customers_is_active",
            table: "customers",
            column: "is_active");

        migrationBuilder.CreateIndex(
            name: "IX_customers_name_is_active",
            table: "customers",
            columns: new[] { "name", "is_active" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("customers");
    }
}
