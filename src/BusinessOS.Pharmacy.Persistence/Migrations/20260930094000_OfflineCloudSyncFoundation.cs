using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20260930094000_OfflineCloudSyncFoundation")]
public sealed class OfflineCloudSyncFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_checkpoints",
            columns: table => new
            {
                stream = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                checkpoint = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_checkpoints", x => x.stream);
            });

        migrationBuilder.CreateTable(
            name: "sync_conflicts",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                queue_item_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                stream = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                entity_id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                consistency_class = table.Column<int>(type: "INTEGER", nullable: false),
                local_payload_json = table.Column<string>(type: "TEXT", nullable: true),
                remote_payload_json = table.Column<string>(type: "TEXT", nullable: true),
                reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                detected_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                resolved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_conflicts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "sync_queue",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                stream = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                entity_id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                operation = table.Column<int>(type: "INTEGER", nullable: false),
                consistency_class = table.Column<int>(type: "INTEGER", nullable: false),
                payload_json = table.Column<string>(type: "TEXT", nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                local_version = table.Column<long>(type: "INTEGER", nullable: false),
                attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                state = table.Column<int>(type: "INTEGER", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                claimed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                last_error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                cloud_entity_id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                cloud_version = table.Column<string>(type: "TEXT", maxLength: 191, nullable: true),
                synced_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_queue", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sync_conflicts_idempotency_key",
            table: "sync_conflicts",
            column: "idempotency_key");

        migrationBuilder.CreateIndex(
            name: "IX_sync_conflicts_status_detected_at",
            table: "sync_conflicts",
            columns: new[] { "status", "detected_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sync_conflicts_stream_entity_id",
            table: "sync_conflicts",
            columns: new[] { "stream", "entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sync_queue_idempotency_key",
            table: "sync_queue",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sync_queue_occurred_at",
            table: "sync_queue",
            column: "occurred_at");

        migrationBuilder.CreateIndex(
            name: "IX_sync_queue_state_next_attempt_at",
            table: "sync_queue",
            columns: new[] { "state", "next_attempt_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sync_queue_stream_entity_id",
            table: "sync_queue",
            columns: new[] { "stream", "entity_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_checkpoints");
        migrationBuilder.DropTable(name: "sync_conflicts");
        migrationBuilder.DropTable(name: "sync_queue");
    }
}
