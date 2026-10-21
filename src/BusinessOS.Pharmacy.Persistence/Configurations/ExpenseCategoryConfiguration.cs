using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategoryEntity>
{
    public void Configure(EntityTypeBuilder<ExpenseCategoryEntity> b)
    {
        b.ToTable("expense_categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedName).HasColumnName("normalized_name").HasMaxLength(100).IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.HasIndex(x => x.NormalizedName).IsUnique();
    }
}
