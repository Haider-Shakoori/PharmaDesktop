using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLineEntity>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLineEntity> builder)
    {
        builder.ToTable("purchase_order_lines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.MedicineId).HasColumnName("medicine_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(255).IsRequired();
        builder.Property(x => x.OrderedQuantity).HasColumnName("ordered_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.LandedCostAllocated).HasColumnName("landed_cost_allocated").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.LineTotal).HasColumnName("line_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.PurchaseOrderId, x.MedicineId });

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Medicine)
            .WithMany()
            .HasForeignKey(x => x.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
