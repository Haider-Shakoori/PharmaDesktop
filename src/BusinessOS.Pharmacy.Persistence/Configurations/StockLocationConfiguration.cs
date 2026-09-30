using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class StockLocationConfiguration : IEntityTypeConfiguration<StockLocationEntity>
{
    public void Configure(EntityTypeBuilder<StockLocationEntity> builder)
    {
        builder.ToTable("stock_locations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.BranchId).HasColumnName("branch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
        builder.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        builder.HasIndex(x => x.IsDefault);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.BranchId, x.IsActive });
        builder.HasOne(x => x.Branch)
            .WithMany(x => x.StockLocations)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
