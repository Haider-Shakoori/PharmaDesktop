using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SyncQueueConfiguration : IEntityTypeConfiguration<SyncQueueEntity>
{
    public void Configure(EntityTypeBuilder<SyncQueueEntity> builder)
    {
        builder.ToTable("sync_queue");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(80).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Operation).HasColumnName("operation").IsRequired();
        builder.Property(x => x.ConsistencyClass).HasColumnName("consistency_class").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.LocalVersion).HasColumnName("local_version").IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.State).HasColumnName("state").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(x => x.CloudEntityId).HasColumnName("cloud_entity_id").HasMaxLength(80);
        builder.Property(x => x.CloudVersion).HasColumnName("cloud_version").HasMaxLength(191);
        builder.Property(x => x.SyncedAt).HasColumnName("synced_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.State, x.NextAttemptAt });
        builder.HasIndex(x => new { x.Stream, x.EntityId });
        builder.HasIndex(x => x.OccurredAt);
    }
}
