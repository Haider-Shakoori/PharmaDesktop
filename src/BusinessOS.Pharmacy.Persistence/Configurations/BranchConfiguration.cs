using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<BranchEntity>
{
    public void Configure(EntityTypeBuilder<BranchEntity> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(500);
        builder.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsDefault);
        builder.HasIndex(x => x.IsActive);
    }
}
