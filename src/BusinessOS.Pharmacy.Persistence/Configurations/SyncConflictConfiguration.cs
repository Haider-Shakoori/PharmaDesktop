using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflictEntity>
{
    public void Configure(EntityTypeBuilder<SyncConflictEntity> builder)
    {
        builder.ToTable("sync_conflicts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(16).IsRequired();
        builder.Property(x => x.QueueItemId).HasColumnName("queue_item_id").HasMaxLength(36);
        builder.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(80).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.ConsistencyClass).HasColumnName("consistency_class").IsRequired();
        builder.Property(x => x.LocalPayloadJson).HasColumnName("local_payload_json");
        builder.Property(x => x.RemotePayloadJson).HasColumnName("remote_payload_json");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.DetectedAt).HasColumnName("detected_at").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.Status, x.DetectedAt });
        builder.HasIndex(x => new { x.Stream, x.EntityId });
        builder.HasIndex(x => x.IdempotencyKey);
    }
}
