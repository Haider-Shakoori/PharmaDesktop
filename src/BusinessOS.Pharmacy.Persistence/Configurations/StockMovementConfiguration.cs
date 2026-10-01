using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovementEntity>
{
    public void Configure(EntityTypeBuilder<StockMovementEntity> builder)
    {
        builder.ToTable("stock_movements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.ProductBatchId).HasColumnName("product_batch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.MedicineId).HasColumnName("medicine_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("branch_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.MovementType).HasColumnName("movement_type").HasMaxLength(48).IsRequired();
        builder.Property(x => x.QuantityDelta).HasColumnName("quantity_delta").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(20, 4);
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceLineId).HasColumnName("source_line_id").HasMaxLength(64);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(255);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(64);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnName("metadata");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.MovementType);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.SourceType, x.SourceId });
        builder.HasIndex(x => new { x.MedicineId, x.OccurredAt });
        builder.HasIndex(x => new { x.ProductBatchId, x.OccurredAt });

        builder.HasOne(x => x.ProductBatch)
            .WithMany(x => x.Movements)
            .HasForeignKey(x => x.ProductBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
