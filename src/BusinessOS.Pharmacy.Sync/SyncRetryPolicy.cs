namespace BusinessOS.Pharmacy.Sync;

public sealed class SyncRetryPolicy
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(15);

    public DateTimeOffset GetNextAttempt(
        DateTimeOffset now,
        int attemptCount)
    {
        if (attemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        var exponent = Math.Min(attemptCount - 1, 12);
        var seconds = InitialDelay.TotalSeconds * Math.Pow(2, exponent);
        var delay = TimeSpan.FromSeconds(
            Math.Min(seconds, MaximumDelay.TotalSeconds));

        return now.Add(delay);
    }
}
