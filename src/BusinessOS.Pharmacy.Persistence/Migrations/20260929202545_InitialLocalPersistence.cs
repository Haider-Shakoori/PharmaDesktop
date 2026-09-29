using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLocalPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_database_identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastOpenedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_database_identity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "local_sequences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CurrentValue = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_sequences", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "local_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_settings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_database_identity_TenantId",
                table: "local_database_identity",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_database_identity");

            migrationBuilder.DropTable(
                name: "local_sequences");

            migrationBuilder.DropTable(
                name: "local_settings");
        }
    }
}
