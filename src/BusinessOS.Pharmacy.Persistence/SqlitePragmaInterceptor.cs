using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BusinessOS.Pharmacy.Persistence;

internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=10000;
            PRAGMA temp_store=MEMORY;
            PRAGMA cache_size=-8192;
            PRAGMA mmap_size=67108864;
            """;
        command.ExecuteNonQuery();
    }

    private static async Task ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=10000;
            PRAGMA temp_store=MEMORY;
            PRAGMA cache_size=-8192;
            PRAGMA mmap_size=67108864;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
