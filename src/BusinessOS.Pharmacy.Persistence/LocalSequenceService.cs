using System.Data;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalSequenceService : ILocalSequenceService
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public LocalSequenceService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<long> NextAsync(
        string sequenceKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceKey);

        if (sequenceKey.Length > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceKey));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO local_sequences ("Key", "CurrentValue", "UpdatedAt")
            VALUES ($key, 1, $updatedAt)
            ON CONFLICT("Key") DO UPDATE SET
                "CurrentValue" = "CurrentValue" + 1,
                "UpdatedAt" = excluded."UpdatedAt"
            RETURNING "CurrentValue";
            """;

        var keyParameter = command.CreateParameter();
        keyParameter.ParameterName = "$key";
        keyParameter.Value = sequenceKey;
        command.Parameters.Add(keyParameter);

        var updatedAtParameter = command.CreateParameter();
        updatedAtParameter.ParameterName = "$updatedAt";
        updatedAtParameter.Value = _clock.UtcNow.ToString("O");
        command.Parameters.Add(updatedAtParameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
