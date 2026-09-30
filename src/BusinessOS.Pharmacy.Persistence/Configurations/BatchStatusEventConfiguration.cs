using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class BatchStatusEventConfiguration : IEntityTypeConfiguration<BatchStatusEventEntity>
{
    public void Configure(EntityTypeBuilder<BatchStatusEventEntity> builder)
    {
        builder.ToTable("batch_status_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.ProductBatchId).HasColumnName("product_batch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(64);
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.ChangedAt);

        builder.HasOne(x => x.ProductBatch)
            .WithMany(x => x.StatusEvents)
            .HasForeignKey(x => x.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
