using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class MedicineConfiguration : IEntityTypeConfiguration<MedicineEntity>
{
    public void Configure(EntityTypeBuilder<MedicineEntity> builder)
    {
        builder.ToTable("medicines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.MedicineCategoryId).HasMaxLength(36);
        builder.Property(x => x.ManufacturerId).HasMaxLength(36);
        builder.Property(x => x.MedicineCode).HasMaxLength(80).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(120).UseCollation("NOCASE");
        builder.Property(x => x.BrandName).HasMaxLength(180).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.GenericName).HasMaxLength(180).UseCollation("NOCASE");
        builder.Property(x => x.Strength).HasMaxLength(100);
        builder.Property(x => x.DosageForm).HasMaxLength(80);
        builder.Property(x => x.PurchaseUnit).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SaleUnit).HasMaxLength(50).IsRequired();
        builder.Property(x => x.UnitsPerPurchaseUnit).HasPrecision(12, 4).IsRequired();
        builder.Property(x => x.ReorderLevel).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.PrescriptionRequired).IsRequired();
        builder.Property(x => x.BatchTrackingRequired).IsRequired();
        builder.Property(x => x.ExpiryTrackingRequired).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.MedicineCode).IsUnique();
        builder.HasIndex(x => x.Barcode).IsUnique();
        builder.HasIndex(x => x.BrandName);
        builder.HasIndex(x => x.GenericName);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.MedicineCategoryId, x.IsActive });

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Medicines)
            .HasForeignKey(x => x.MedicineCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Manufacturer)
            .WithMany(x => x.Medicines)
            .HasForeignKey(x => x.ManufacturerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
