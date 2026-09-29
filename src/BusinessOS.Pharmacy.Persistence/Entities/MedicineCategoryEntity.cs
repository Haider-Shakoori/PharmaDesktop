namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class MedicineCategoryEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<MedicineEntity> Medicines { get; set; } = [];
}
