namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public sealed class InventoryValidationException : Exception
{
    public InventoryValidationException(string message)
        : base(message)
    {
    }
}
