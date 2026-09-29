using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class LocalSequenceConfiguration : IEntityTypeConfiguration<LocalSequenceEntity>
{
    public void Configure(EntityTypeBuilder<LocalSequenceEntity> builder)
    {
        builder.ToTable("local_sequences");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(120);
        builder.Property(x => x.CurrentValue).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
