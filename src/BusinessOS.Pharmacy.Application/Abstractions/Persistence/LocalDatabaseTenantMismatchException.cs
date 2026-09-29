namespace BusinessOS.Pharmacy.Application.Abstractions.Persistence;

public sealed class LocalDatabaseTenantMismatchException : Exception
{
    public LocalDatabaseTenantMismatchException(string expectedTenantId, string actualTenantId)
        : base("The local pharmacy database belongs to a different tenant and cannot be opened by this activation.")
    {
        ExpectedTenantId = expectedTenantId;
        ActualTenantId = actualTenantId;
    }

    public string ExpectedTenantId { get; }
    public string ActualTenantId { get; }
}
