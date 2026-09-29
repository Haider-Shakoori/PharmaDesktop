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
        using var synchronous = connection.CreateCommand();
        synchronous.CommandText = "PRAGMA synchronous=NORMAL;";
        synchronous.ExecuteNonQuery();

        using var busyTimeout = connection.CreateCommand();
        busyTimeout.CommandText = "PRAGMA busy_timeout=5000;";
        busyTimeout.ExecuteNonQuery();
    }

    private static async Task ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var synchronous = connection.CreateCommand();
        synchronous.CommandText = "PRAGMA synchronous=NORMAL;";
        await synchronous.ExecuteNonQueryAsync(cancellationToken);

        await using var busyTimeout = connection.CreateCommand();
        busyTimeout.CommandText = "PRAGMA busy_timeout=5000;";
        await busyTimeout.ExecuteNonQueryAsync(cancellationToken);
    }
}
