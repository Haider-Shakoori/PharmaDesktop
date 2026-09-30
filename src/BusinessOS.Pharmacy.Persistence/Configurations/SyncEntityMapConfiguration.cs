using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SyncEntityMapConfiguration : IEntityTypeConfiguration<SyncEntityMapEntity>
{
    public void Configure(EntityTypeBuilder<SyncEntityMapEntity> builder)
    {
        builder.ToTable("sync_entity_maps");
        builder.HasKey(x => new { x.Stream, x.LocalEntityId });

        builder.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(80);
        builder.Property(x => x.LocalEntityId).HasColumnName("local_entity_id").HasMaxLength(80);
        builder.Property(x => x.CloudEntityId).HasColumnName("cloud_entity_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CloudVersion).HasColumnName("cloud_version").HasMaxLength(191);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.Stream, x.CloudEntityId }).IsUnique();
    }
}
