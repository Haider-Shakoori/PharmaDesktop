using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<SaleEntity>
{
    public void Configure(EntityTypeBuilder<SaleEntity> builder)
    {
        builder.ToTable("sales");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleNumber).HasColumnName("sale_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").HasMaxLength(36);
        builder.Property(x => x.PrescriptionReference).HasColumnName("prescription_reference").HasMaxLength(120);
        builder.Property(x => x.PrescriberName).HasColumnName("prescriber_name").HasMaxLength(160);
        builder.Property(x => x.PrescriptionDate).HasColumnName("prescription_date");
        builder.Property(x => x.BusinessDate).HasColumnName("business_date").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.DiscountTotal).HasColumnName("discount_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.TaxTotal).HasColumnName("tax_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.GrandTotal).HasColumnName("grand_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.PaidTotal).HasColumnName("paid_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.DueTotal).HasColumnName("due_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.ChangeTotal).HasColumnName("change_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.PaymentStatus).HasColumnName("payment_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.HeldAt).HasColumnName("held_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.SaleNumber).IsUnique();
        builder.HasIndex(x => x.PrescriptionReference);
        builder.HasIndex(x => x.BusinessDate);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PaymentStatus);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.CompletedAt);
        builder.HasIndex(x => new { x.BusinessDate, x.Status });
        builder.HasIndex(x => new { x.CreatedBy, x.BusinessDate });

        builder.HasOne(x => x.StockLocation)
            .WithMany()
            .HasForeignKey(x => x.StockLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
