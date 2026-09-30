using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class InventoryAdjustmentLineConfiguration : IEntityTypeConfiguration<InventoryAdjustmentLineEntity>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustmentLineEntity> builder)
    {
        builder.ToTable("inventory_adjustment_lines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.InventoryAdjustmentId).HasColumnName("inventory_adjustment_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.ProductBatchId).HasColumnName("product_batch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.QuantityDelta).HasColumnName("quantity_delta").HasColumnType("NUMERIC").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.InventoryAdjustmentId, x.ProductBatchId });
        builder.HasOne(x => x.InventoryAdjustment)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.InventoryAdjustmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductBatch)
            .WithMany()
            .HasForeignKey(x => x.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
