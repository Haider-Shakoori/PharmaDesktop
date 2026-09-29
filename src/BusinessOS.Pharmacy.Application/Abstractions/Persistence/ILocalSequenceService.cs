namespace BusinessOS.Pharmacy.Application.Abstractions.Persistence;

public interface ILocalSequenceService
{
    Task<long> NextAsync(
        string sequenceKey,
        CancellationToken cancellationToken = default);
}
