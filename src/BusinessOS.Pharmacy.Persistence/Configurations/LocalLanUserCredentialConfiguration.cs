using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class LocalLanUserCredentialConfiguration : IEntityTypeConfiguration<LocalLanUserCredentialEntity>
{
    public void Configure(EntityTypeBuilder<LocalLanUserCredentialEntity> builder)
    {
        builder.ToTable("local_lan_user_credentials");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.RolesJson).HasColumnName("roles_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.PermissionsJson).HasColumnName("permissions_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.PasswordSaltBase64).HasColumnName("password_salt_base64").HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordHashBase64).HasColumnName("password_hash_base64").HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordIterations).HasColumnName("password_iterations").IsRequired();
        builder.Property(x => x.LastOnlineVerifiedAt).HasColumnName("last_online_verified_at").IsRequired();
        builder.Property(x => x.IdentityValidUntil).HasColumnName("identity_valid_until").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}
