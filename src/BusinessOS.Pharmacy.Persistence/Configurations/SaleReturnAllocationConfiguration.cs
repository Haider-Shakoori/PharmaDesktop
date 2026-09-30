using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleReturnAllocationConfiguration : IEntityTypeConfiguration<SaleReturnAllocationEntity>
{
    public void Configure(EntityTypeBuilder<SaleReturnAllocationEntity> builder)
    {
        builder.ToTable("sale_return_allocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleReturnLineId).HasColumnName("sale_return_line_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.SaleBatchAllocationId).HasColumnName("sale_batch_allocation_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.ProductBatchId).HasColumnName("product_batch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.StockMovementId).HasColumnName("stock_movement_id").HasMaxLength(36);
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Restocked).HasColumnName("restocked").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.SaleBatchAllocationId, x.SaleReturnLineId }).HasDatabaseName("sale_return_alloc_batch_line_idx");
        builder.HasOne(x => x.SaleReturnLine).WithMany(x => x.Allocations).HasForeignKey(x => x.SaleReturnLineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SaleBatchAllocation).WithMany().HasForeignKey(x => x.SaleBatchAllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductBatch).WithMany().HasForeignKey(x => x.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovementEntity>().WithMany().HasForeignKey(x => x.StockMovementId).OnDelete(DeleteBehavior.Restrict);
    }
}
