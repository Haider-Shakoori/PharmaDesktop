using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleReturnRefundConfiguration : IEntityTypeConfiguration<SaleReturnRefundEntity>
{
    public void Configure(EntityTypeBuilder<SaleReturnRefundEntity> builder)
    {
        builder.ToTable("sale_return_refunds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleReturnId).HasColumnName("sale_return_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(160);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.Method);
        builder.HasIndex(x => new { x.SaleReturnId, x.Method });
        builder.HasOne(x => x.SaleReturn).WithMany(x => x.Refunds).HasForeignKey(x => x.SaleReturnId).OnDelete(DeleteBehavior.Restrict);
    }
}
