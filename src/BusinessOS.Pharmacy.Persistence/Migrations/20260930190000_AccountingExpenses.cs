using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    journal_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    source_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    source_event = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    source_number = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                    reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    total_debit = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    total_credit = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    posted_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    reversal_of_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    reversal_reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_entries_journal_entries_reversal_of_id",
                        column: x => x.reversal_of_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    parent_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    normal_balance = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    system_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    is_system = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_ledger_accounts_ledger_accounts_parent_id",
                        column: x => x.parent_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    expense_number = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    expense_account_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    payment_account_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    payee = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                    created_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.id);
                    table.ForeignKey(
                        name: "FK_expenses_ledger_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_ledger_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_stock_locations_stock_location_id",
                        column: x => x.stock_location_id,
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    journal_entry_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ledger_account_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    stock_location_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    debit = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    credit = table.Column<decimal>(type: "TEXT", precision: 20, scale: 4, nullable: false),
                    counterparty_type = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    counterparty_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    memo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_journal_lines_ledger_accounts_ledger_account_id",
                        column: x => x.ledger_account_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_stock_locations_stock_location_id",
                        column: x => x.stock_location_id,
                        principalTable: "stock_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_business_date",
                table: "expenses",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_expense_account_id",
                table: "expenses",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_expense_number",
                table: "expenses",
                column: "expense_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expenses_idempotency_key",
                table: "expenses",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expenses_payment_account_id",
                table: "expenses",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_posted_at",
                table: "expenses",
                column: "posted_at");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_reversed_at",
                table: "expenses",
                column: "reversed_at");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_status",
                table: "expenses",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_stock_location_id",
                table: "expenses",
                column: "stock_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_date",
                table: "journal_entries",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_date_status",
                table: "journal_entries",
                columns: new[] { "business_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_idempotency_key",
                table: "journal_entries",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_journal_number",
                table: "journal_entries",
                column: "journal_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_occurred_at",
                table: "journal_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_posted_at",
                table: "journal_entries",
                column: "posted_at");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_reversal_of_id",
                table: "journal_entries",
                column: "reversal_of_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_source_event",
                table: "journal_entries",
                column: "source_event");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_source_id",
                table: "journal_entries",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_source_type",
                table: "journal_entries",
                column: "source_type");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_source_type_source_id_source_event",
                table: "journal_entries",
                columns: new[] { "source_type", "source_id", "source_event" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_status",
                table: "journal_entries",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_counterparty_type_counterparty_id",
                table: "journal_lines",
                columns: new[] { "counterparty_type", "counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_journal_entry_id",
                table: "journal_lines",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_ledger_account_id_journal_entry_id",
                table: "journal_lines",
                columns: new[] { "ledger_account_id", "journal_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_stock_location_id_ledger_account_id",
                table: "journal_lines",
                columns: new[] { "stock_location_id", "ledger_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_code",
                table: "ledger_accounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_is_active",
                table: "ledger_accounts",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_is_system",
                table: "ledger_accounts",
                column: "is_system");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_parent_id",
                table: "ledger_accounts",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_system_key",
                table: "ledger_accounts",
                column: "system_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_type",
                table: "ledger_accounts",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_type_is_active",
                table: "ledger_accounts",
                columns: new[] { "type", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "ledger_accounts");
        }
    }
}
