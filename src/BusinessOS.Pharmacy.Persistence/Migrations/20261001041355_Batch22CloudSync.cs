using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Batch22CloudSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cloud_sync_cursors",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    stream = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    cursor = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_sync_cursors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cloud_sync_outbox",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    last_error_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    last_error_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    server_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    server_updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_sync_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cloud_sync_remote_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    stream = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    server_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    server_updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_sync_remote_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_cursors_tenant_id_stream",
                table: "cloud_sync_cursors",
                columns: new[] { "tenant_id", "stream" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_outbox_created_at",
                table: "cloud_sync_outbox",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_outbox_tenant_id_actor_user_id_status_next_attempt_at",
                table: "cloud_sync_outbox",
                columns: new[] { "tenant_id", "actor_user_id", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_outbox_tenant_id_idempotency_key",
                table: "cloud_sync_outbox",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_remote_records_tenant_id_stream_server_id",
                table: "cloud_sync_remote_records",
                columns: new[] { "tenant_id", "stream", "server_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_remote_records_tenant_id_stream_server_updated_at",
                table: "cloud_sync_remote_records",
                columns: new[] { "tenant_id", "stream", "server_updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cloud_sync_cursors");

            migrationBuilder.DropTable(
                name: "cloud_sync_outbox");

            migrationBuilder.DropTable(
                name: "cloud_sync_remote_records");
        }
    }
}
