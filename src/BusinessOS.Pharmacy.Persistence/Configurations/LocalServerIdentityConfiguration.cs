using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class LocalServerIdentityConfiguration : IEntityTypeConfiguration<LocalServerIdentityEntity>
{
    public void Configure(EntityTypeBuilder<LocalServerIdentityEntity> builder)
    {
        builder.ToTable("local_server_identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ServerId).HasColumnName("server_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ServerName).HasColumnName("server_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.ServerId).IsUnique();
    }
}
