using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class LocalSettingConfiguration : IEntityTypeConfiguration<LocalSettingEntity>
{
    public void Configure(EntityTypeBuilder<LocalSettingEntity> builder)
    {
        builder.ToTable("local_settings");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(160);
        builder.Property(x => x.Value).HasMaxLength(4000);
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
