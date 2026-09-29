using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class SqliteSmokeTests
{
    [Fact]
    public async Task PharmacyDbContext_CanOpenSqliteDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BusinessOS.Pharmacy.Persistence.PharmacyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new BusinessOS.Pharmacy.Persistence.PharmacyDbContext(options);
        Assert.True(await context.Database.CanConnectAsync());
    }
}
