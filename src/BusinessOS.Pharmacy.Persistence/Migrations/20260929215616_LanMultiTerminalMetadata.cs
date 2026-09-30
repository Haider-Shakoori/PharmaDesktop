using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LanMultiTerminalMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_server_identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    server_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    server_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_server_identity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "network_audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    operation = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    outcome = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    record_uuid = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    remote_address = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_network_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registered_terminals",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    computer_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    terminal_role = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    secret_hash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    allowed_permissions_json = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_terminals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminal_pairing_codes",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    salt_base64 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    code_hash_base64 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    failed_attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    max_attempts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_terminal_pairing_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_server_identity_server_id",
                table: "local_server_identity",
                column: "server_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_network_audit_log_occurred_at",
                table: "network_audit_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_network_audit_log_terminal_id",
                table: "network_audit_log",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "IX_registered_terminals_computer_name",
                table: "registered_terminals",
                column: "computer_name");

            migrationBuilder.CreateIndex(
                name: "IX_registered_terminals_is_active",
                table: "registered_terminals",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_terminal_pairing_codes_expires_at",
                table: "terminal_pairing_codes",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_server_identity");

            migrationBuilder.DropTable(
                name: "network_audit_log");

            migrationBuilder.DropTable(
                name: "registered_terminals");

            migrationBuilder.DropTable(
                name: "terminal_pairing_codes");
        }
    }
}
