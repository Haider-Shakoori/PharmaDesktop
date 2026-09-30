using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SaleReturnLineConfiguration : IEntityTypeConfiguration<SaleReturnLineEntity>
{
    public void Configure(EntityTypeBuilder<SaleReturnLineEntity> builder)
    {
        builder.ToTable("sale_return_lines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SaleReturnId).HasColumnName("sale_return_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.SaleLineId).HasColumnName("sale_line_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.MedicineId).HasColumnName("medicine_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.RefundAmount).HasColumnName("refund_amount").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.Disposition).HasColumnName("disposition").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.SaleLineId, x.SaleReturnId });
        builder.HasOne(x => x.SaleReturn).WithMany(x => x.Lines).HasForeignKey(x => x.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SaleLine).WithMany().HasForeignKey(x => x.SaleLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Medicine).WithMany().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
    }
}
