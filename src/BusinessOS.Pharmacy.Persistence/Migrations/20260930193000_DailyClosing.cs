using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailyClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cashier_shifts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    opening_cash = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    expected_cash = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                    counted_cash = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                    variance = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    closing_notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cashier_shifts", x => x.id);
                    table.ForeignKey(
                        name: "FK_cashier_shifts_stock_locations_stock_location_id",
                        column: x => x.stock_location_id,
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_closings",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    gross_sales = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    discount_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    returns_total = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    cash_collected = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    bank_collected = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    mobile_collected = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    credit_sales = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    opening_cash = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    expected_cash = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    counted_cash = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                    variance = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: true),
                    finalized_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    approved_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    reopened_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    finalized_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    reopened_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    closing_notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    reopen_reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_closings", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_closings_stock_locations_stock_location_id",
                        column: x => x.stock_location_id,
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_closing_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    daily_closing_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    actor_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    snapshot = table.Column<string>(type: "TEXT", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_closing_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_closing_events_daily_closings_daily_closing_id",
                        column: x => x.daily_closing_id,
                        principalTable: "daily_closings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_business_date",
                table: "cashier_shifts",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_closed_at",
                table: "cashier_shifts",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_opened_at",
                table: "cashier_shifts",
                column: "opened_at");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_status",
                table: "cashier_shifts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_stock_location_id_business_date_status",
                table: "cashier_shifts",
                columns: new[] { "stock_location_id", "business_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_cashier_shifts_user_id_business_date_status",
                table: "cashier_shifts",
                columns: new[] { "user_id", "business_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_closing_events_daily_closing_id",
                table: "daily_closing_events",
                column: "daily_closing_id");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closing_events_event_type",
                table: "daily_closing_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closing_events_occurred_at",
                table: "daily_closing_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closings_approved_at",
                table: "daily_closings",
                column: "approved_at");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closings_finalized_at",
                table: "daily_closings",
                column: "finalized_at");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closings_reopened_at",
                table: "daily_closings",
                column: "reopened_at");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closings_status",
                table: "daily_closings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_daily_closings_stock_location_id_business_date",
                table: "daily_closings",
                columns: new[] { "stock_location_id", "business_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cashier_shifts");

            migrationBuilder.DropTable(
                name: "daily_closing_events");

            migrationBuilder.DropTable(
                name: "daily_closings");
        }
    }
}
