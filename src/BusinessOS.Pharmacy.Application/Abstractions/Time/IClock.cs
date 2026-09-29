namespace BusinessOS.Pharmacy.Application.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
