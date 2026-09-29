using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260929234857_LanUserIdentityValidity")]
public sealed class LanUserIdentityValidity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "identity_valid_until",
            table: "local_lan_user_credentials",
            type: "TEXT",
            nullable: false,
            defaultValue: DateTimeOffset.MinValue);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "identity_valid_until",
            table: "local_lan_user_credentials");
    }
}
