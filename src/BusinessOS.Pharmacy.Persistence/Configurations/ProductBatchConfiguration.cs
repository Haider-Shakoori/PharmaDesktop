using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatchEntity>
{
    public void Configure(EntityTypeBuilder<ProductBatchEntity> builder)
    {
        builder.ToTable("product_batches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.MedicineId).HasColumnName("medicine_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").HasMaxLength(36);
        builder.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasMaxLength(36);
        builder.Property(x => x.GoodsReceiptId).HasColumnName("goods_receipt_id").HasMaxLength(36);
        builder.Property(x => x.BranchId).HasColumnName("branch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(120);
        builder.Property(x => x.BatchKey).HasColumnName("batch_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.ManufacturedAt).HasColumnName("manufactured_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.AvailableQuantity).HasColumnName("available_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PurchaseCost).HasColumnName("purchase_cost").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.SalePrice).HasColumnName("sale_price").HasPrecision(20, 4);
        builder.Property(x => x.LastMovementAt).HasColumnName("last_movement_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.BatchNumber);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.LastMovementAt);
        builder.HasIndex(x => new { x.MedicineId, x.StockLocationId, x.BatchKey }).IsUnique();
        builder.HasIndex(x => new { x.MedicineId, x.Status, x.ExpiresAt });
        builder.HasIndex(x => new { x.StockLocationId, x.Status, x.ExpiresAt });

        builder.HasOne(x => x.Medicine)
            .WithMany()
            .HasForeignKey(x => x.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany(x => x.ProductBatches)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StockLocation)
            .WithMany(x => x.ProductBatches)
            .HasForeignKey(x => x.StockLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
