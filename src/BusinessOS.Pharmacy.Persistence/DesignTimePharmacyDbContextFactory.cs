using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class DesignTimePharmacyDbContextFactory : IDesignTimeDbContextFactory<PharmacyDbContext>
{
    public PharmacyDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "businessos-pharmacy-design.db");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();

        var options = new DbContextOptionsBuilder<PharmacyDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new PharmacyDbContext(options);
    }
}
