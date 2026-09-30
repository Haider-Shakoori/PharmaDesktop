using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleBatchAllocationConfiguration : IEntityTypeConfiguration<SaleBatchAllocationEntity>
{
    public void Configure(EntityTypeBuilder<SaleBatchAllocationEntity> builder)
    {
        builder.ToTable("sale_batch_allocations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleLineId).HasColumnName("sale_line_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.ProductBatchId).HasColumnName("product_batch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.StockMovementId).HasColumnName("stock_movement_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.LineTotal).HasColumnName("line_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.StockMovementId).IsUnique();
        builder.HasIndex(x => new { x.SaleLineId, x.ProductBatchId });

        builder.HasOne(x => x.SaleLine)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.SaleLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ProductBatch)
            .WithMany()
            .HasForeignKey(x => x.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockMovementEntity>()
            .WithMany()
            .HasForeignKey(x => x.StockMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
