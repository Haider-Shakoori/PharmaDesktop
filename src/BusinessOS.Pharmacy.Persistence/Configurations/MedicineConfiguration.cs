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

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.MedicineCategoryId).HasColumnName("medicine_category_id").HasMaxLength(36);
        builder.Property(x => x.ManufacturerId).HasColumnName("manufacturer_id").HasMaxLength(36);
        builder.Property(x => x.MedicineCode).HasColumnName("medicine_code").HasMaxLength(80).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(120).UseCollation("NOCASE");
        builder.Property(x => x.BrandName).HasColumnName("brand_name").HasMaxLength(180).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.GenericName).HasColumnName("generic_name").HasMaxLength(180).UseCollation("NOCASE");
        builder.Property(x => x.Strength).HasColumnName("strength").HasMaxLength(100);
        builder.Property(x => x.DosageForm).HasColumnName("dosage_form").HasMaxLength(80);
        builder.Property(x => x.PurchaseUnit).HasColumnName("purchase_unit").HasMaxLength(50).IsRequired();
        builder.Property(x => x.SaleUnit).HasColumnName("sale_unit").HasMaxLength(50).IsRequired();
        builder.Property(x => x.UnitsPerPurchaseUnit).HasColumnName("units_per_purchase_unit").HasPrecision(12, 4).IsRequired();
        builder.Property(x => x.ReorderLevel).HasColumnName("reorder_level").HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.PrescriptionRequired).HasColumnName("prescription_required").IsRequired();
        builder.Property(x => x.BatchTrackingRequired).HasColumnName("batch_tracking_required").IsRequired();
        builder.Property(x => x.ExpiryTrackingRequired).HasColumnName("expiry_tracking_required").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

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
