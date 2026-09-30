using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturnEntity>
{
    public void Configure(EntityTypeBuilder<SaleReturnEntity> builder)
    {
        builder.ToTable("sale_returns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.ReturnNumber).HasColumnName("return_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.SaleId).HasColumnName("sale_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.BusinessDate).HasColumnName("business_date").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.RefundTotal).HasColumnName("refund_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.ReturnNumber).IsUnique();
        builder.HasIndex(x => x.BusinessDate);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.CompletedAt);
        builder.HasIndex(x => new { x.SaleId, x.Status });
        builder.HasIndex(x => new { x.StockLocationId, x.BusinessDate });
        builder.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockLocation).WithMany().HasForeignKey(x => x.StockLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
