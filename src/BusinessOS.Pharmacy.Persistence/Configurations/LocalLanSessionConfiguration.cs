using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class LocalLanSessionConfiguration : IEntityTypeConfiguration<LocalLanSessionEntity>
{
    public void Configure(EntityTypeBuilder<LocalLanSessionEntity> builder)
    {
        builder.ToTable("local_lan_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.TerminalId).HasColumnName("terminal_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(x => x.RolesJson).HasColumnName("roles_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.PermissionsJson).HasColumnName("permissions_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.TerminalId, x.ExpiresAt });
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt });
    }
}
