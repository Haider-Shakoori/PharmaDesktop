using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SyncCheckpointConfiguration : IEntityTypeConfiguration<SyncCheckpointEntity>
{
    public void Configure(EntityTypeBuilder<SyncCheckpointEntity> builder)
    {
        builder.ToTable("sync_checkpoints");
        builder.HasKey(x => x.Stream);
        builder.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(80);
        builder.Property(x => x.Checkpoint).HasColumnName("checkpoint").HasMaxLength(1000);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
