using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePaymentEntity>
{
    public void Configure(EntityTypeBuilder<SalePaymentEntity> builder)
    {
        builder.ToTable("sale_payments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleId).HasColumnName("sale_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.PaymentNumber).HasColumnName("payment_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(160);
        builder.Property(x => x.PaidAt).HasColumnName("paid_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.PaymentNumber).IsUnique();
        builder.HasIndex(x => x.Method);
        builder.HasIndex(x => x.PaidAt);
        builder.HasIndex(x => new { x.SaleId, x.Method });

        builder.HasOne(x => x.Sale)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
