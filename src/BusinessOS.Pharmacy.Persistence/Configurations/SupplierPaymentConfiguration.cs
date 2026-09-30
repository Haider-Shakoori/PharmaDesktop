using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPaymentEntity>
{
    public void Configure(EntityTypeBuilder<SupplierPaymentEntity> builder)
    {
        builder.ToTable("supplier_payments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.PurchaseInvoiceId).HasColumnName("purchase_invoice_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.PaymentNumber).HasColumnName("payment_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(160);
        builder.Property(x => x.PaidAt).HasColumnName("paid_at").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120);
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.PaymentNumber).IsUnique();
        builder.HasIndex(x => x.PaidAt);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.SupplierId, x.PaidAt });

        builder.HasOne(x => x.PurchaseInvoice)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Supplier)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
