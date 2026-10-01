using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class CloudSyncCursorConfiguration : IEntityTypeConfiguration<CloudSyncCursorEntity>
{
    public void Configure(EntityTypeBuilder<CloudSyncCursorEntity> builder)
    {
        builder.ToTable("cloud_sync_cursors");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Cursor).HasColumnName("cursor").HasMaxLength(1000);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Stream }).IsUnique();
    }
}
