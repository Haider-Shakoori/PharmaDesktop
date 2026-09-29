using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class ManufacturerConfiguration : IEntityTypeConfiguration<ManufacturerEntity>
{
    public void Configure(EntityTypeBuilder<ManufacturerEntity> builder)
    {
        builder.ToTable("manufacturers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(160).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}
