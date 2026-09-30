using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LanUserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_lan_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    terminal_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    roles_json = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    permissions_json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_lan_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "local_lan_user_credentials",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, collation: "NOCASE"),
                    roles_json = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    permissions_json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    password_salt_base64 = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    password_hash_base64 = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    password_iterations = table.Column<int>(type: "INTEGER", nullable: false),
                    last_online_verified_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_lan_user_credentials", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_lan_sessions_terminal_id_expires_at",
                table: "local_lan_sessions",
                columns: new[] { "terminal_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_local_lan_sessions_token_hash",
                table: "local_lan_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_lan_sessions_user_id_expires_at",
                table: "local_lan_sessions",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_local_lan_user_credentials_email",
                table: "local_lan_user_credentials",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_lan_user_credentials_tenant_id_is_active",
                table: "local_lan_user_credentials",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_lan_sessions");

            migrationBuilder.DropTable(
                name: "local_lan_user_credentials");
        }
    }
}
