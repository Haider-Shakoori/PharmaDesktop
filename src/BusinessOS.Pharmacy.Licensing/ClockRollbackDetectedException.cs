namespace BusinessOS.Pharmacy.Licensing;

public sealed class ClockRollbackDetectedException : Exception
{
    public ClockRollbackDetectedException()
        : base("The Windows clock moved backward beyond the allowed tolerance. Online license verification is required.")
    {
    }
}