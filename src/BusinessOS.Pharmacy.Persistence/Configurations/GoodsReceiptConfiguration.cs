using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceiptEntity>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptEntity> builder)
    {
        builder.ToTable("goods_receipts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36);
        builder.Property(x => x.ReceiptNumber).HasColumnName("receipt_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120);
        builder.Property(x => x.InventoryPostedAt).HasColumnName("inventory_posted_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.ReceiptNumber).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ReceivedAt);
        builder.HasIndex(x => x.InventoryPostedAt);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany(x => x.Receipts)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Supplier)
            .WithMany(x => x.GoodsReceipts)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StockLocation)
            .WithMany()
            .HasForeignKey(x => x.StockLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
