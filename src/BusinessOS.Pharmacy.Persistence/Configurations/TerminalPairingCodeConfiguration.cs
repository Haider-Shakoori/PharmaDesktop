using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class TerminalPairingCodeConfiguration : IEntityTypeConfiguration<TerminalPairingCodeEntity>
{
    public void Configure(EntityTypeBuilder<TerminalPairingCodeEntity> builder)
    {
        builder.ToTable("terminal_pairing_codes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.SaltBase64).HasColumnName("salt_base64").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CodeHashBase64).HasColumnName("code_hash_base64").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.UsedAt).HasColumnName("used_at");
        builder.Property(x => x.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.HasIndex(x => x.ExpiresAt);
    }
}
