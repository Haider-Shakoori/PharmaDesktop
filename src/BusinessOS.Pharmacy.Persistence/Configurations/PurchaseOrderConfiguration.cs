using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrderEntity>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderEntity> builder)
    {
        builder.ToTable("purchase_orders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Number).HasColumnName("number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.OrderDate).HasColumnName("order_date").IsRequired();
        builder.Property(x => x.ExpectedDate).HasColumnName("expected_date");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.DiscountTotal).HasColumnName("discount_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.LandedCostTotal).HasColumnName("landed_cost_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.GrandTotal).HasColumnName("grand_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ApprovedBy).HasColumnName("approved_by").HasMaxLength(64);
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.OrderDate);
        builder.HasIndex(x => x.ExpectedDate);
        builder.HasIndex(x => new { x.SupplierId, x.OrderDate });

        builder.HasOne(x => x.Supplier)
            .WithMany(x => x.PurchaseOrders)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
