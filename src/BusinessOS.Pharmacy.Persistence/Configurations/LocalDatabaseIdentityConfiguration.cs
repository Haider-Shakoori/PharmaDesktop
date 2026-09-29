using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class LocalDatabaseIdentityConfiguration : IEntityTypeConfiguration<LocalDatabaseIdentityEntity>
{
    public void Configure(EntityTypeBuilder<LocalDatabaseIdentityEntity> builder)
    {
        builder.ToTable("local_database_identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DatabaseInstanceId).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LastOpenedAt).IsRequired();
        builder.HasIndex(x => x.TenantId).IsUnique();
    }
}
