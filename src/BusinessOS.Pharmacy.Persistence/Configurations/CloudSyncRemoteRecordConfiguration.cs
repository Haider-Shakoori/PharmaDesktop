using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class CloudSyncRemoteRecordConfiguration : IEntityTypeConfiguration<CloudSyncRemoteRecordEntity>
{
    public void Configure(EntityTypeBuilder<CloudSyncRemoteRecordEntity> builder)
    {
        builder.ToTable("cloud_sync_remote_records");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(50).IsRequired();
        builder.Property(x => x.ServerId).HasColumnName("server_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.Property(x => x.ServerUpdatedAt).HasColumnName("server_updated_at");
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Stream, x.ServerId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Stream, x.ServerUpdatedAt });
    }
}
