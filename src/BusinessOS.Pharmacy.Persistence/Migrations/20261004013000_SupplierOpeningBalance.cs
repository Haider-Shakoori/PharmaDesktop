using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261004013000_SupplierOpeningBalance")]
public partial class SupplierOpeningBalance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "opening_balance",
            table: "suppliers",
            type: "TEXT",
            precision: 20,
            scale: 4,
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "opening_balance",
            table: "suppliers");
    }
}
