using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLineEntity>
{
    public void Configure(EntityTypeBuilder<SaleLineEntity> builder)
    {
        builder.ToTable("sale_lines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleId).HasColumnName("sale_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.MedicineId).HasColumnName("medicine_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(255).IsRequired();
        builder.Property(x => x.SaleUnit).HasColumnName("sale_unit").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.LineTotal).HasColumnName("line_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.CostTotal).HasColumnName("cost_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.PrescriptionRequired).HasColumnName("prescription_required").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.SaleId, x.MedicineId });

        builder.HasOne(x => x.Sale)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Medicine)
            .WithMany()
            .HasForeignKey(x => x.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
