using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

internal static class InventoryWriteTransaction
{
    public static async Task<SqliteTransaction> BeginAsync(
        PharmacyDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var transaction = connection.BeginTransaction(SqliteTransactionMode.Immediate);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return transaction;
    }
}
