using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class CloudSyncOutboxConfiguration : IEntityTypeConfiguration<CloudSyncOutboxEntity>
{
    public void Configure(EntityTypeBuilder<CloudSyncOutboxEntity> builder)
    {
        builder.ToTable("cloud_sync_outbox");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(100);
        builder.Property(x => x.LastErrorMessage).HasColumnName("last_error_message").HasMaxLength(2000);
        builder.Property(x => x.ServerId).HasColumnName("server_id").HasMaxLength(100);
        builder.Property(x => x.ServerUpdatedAt).HasColumnName("server_updated_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ActorUserId, x.Status, x.NextAttemptAt });
        builder.HasIndex(x => x.CreatedAt);
    }
}
