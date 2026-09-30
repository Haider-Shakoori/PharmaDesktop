using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLineEntity>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLineEntity> builder)
    {
        builder.ToTable("goods_receipt_lines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.GoodsReceiptId).HasColumnName("goods_receipt_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.PurchaseOrderLineId).HasColumnName("purchase_order_line_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.MedicineId).HasColumnName("medicine_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.BonusQuantity).HasColumnName("bonus_quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(120);
        builder.Property(x => x.ManufacturedAt).HasColumnName("manufactured_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.SalePrice).HasColumnName("sale_price").HasPrecision(20, 4);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.BatchNumber);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => new { x.MedicineId, x.ExpiresAt });

        builder.HasOne(x => x.GoodsReceipt)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PurchaseOrderLine)
            .WithMany(x => x.ReceiptLines)
            .HasForeignKey(x => x.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Medicine)
            .WithMany()
            .HasForeignKey(x => x.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
