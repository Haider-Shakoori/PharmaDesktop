using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class MedicineCategoryConfiguration : IEntityTypeConfiguration<MedicineCategoryEntity>
{
    public void Configure(EntityTypeBuilder<MedicineCategoryEntity> builder)
    {
        builder.ToTable("medicine_categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}
