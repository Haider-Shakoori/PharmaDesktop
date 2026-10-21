using BusinessOS.Pharmacy.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessOS.Pharmacy.Persistence.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261021190000_ExpenseCategories")]
public partial class ExpenseCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "expense_categories",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                normalized_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_expense_categories", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_expense_categories_normalized_name",
            table: "expense_categories",
            column: "normalized_name",
            unique: true);

        // Nullable so every historical expense remains valid and unmodified.
        migrationBuilder.AddColumn<string>(
            name: "category_id",
            table: "expenses",
            type: "TEXT",
            maxLength: 36,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_expenses_category_id",
            table: "expenses",
            column: "category_id");

        // SQLite cannot add a foreign key to an existing table without a table
        // rebuild. Service-layer writes validate active categories, preserving
        // historical expenses while upgrading existing installations.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_expenses_category_id", "expenses");
        migrationBuilder.DropColumn("category_id", "expenses");
        migrationBuilder.DropTable("expense_categories");
    }
}
