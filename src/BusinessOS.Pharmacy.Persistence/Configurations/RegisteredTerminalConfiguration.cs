using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class RegisteredTerminalConfiguration : IEntityTypeConfiguration<RegisteredTerminalEntity>
{
    public void Configure(EntityTypeBuilder<RegisteredTerminalEntity> builder)
    {
        builder.ToTable("registered_terminals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.ComputerName).HasColumnName("computer_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.TerminalRole).HasColumnName("terminal_role").HasMaxLength(80).IsRequired();
        builder.Property(x => x.SecretHash).HasColumnName("secret_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.AllowedPermissionsJson).HasColumnName("allowed_permissions_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.RegisteredAt).HasColumnName("registered_at").IsRequired();
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.ComputerName);
    }
}
