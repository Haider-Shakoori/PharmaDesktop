using BusinessOS.Pharmacy.Application.Abstractions.Time;

namespace BusinessOS.Pharmacy.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
