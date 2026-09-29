using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class NetworkAuditConfiguration : IEntityTypeConfiguration<NetworkAuditEntity>
{
    public void Configure(EntityTypeBuilder<NetworkAuditEntity> builder)
    {
        builder.ToTable("network_audit_log");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(40).IsRequired();
        builder.Property(x => x.TerminalId).HasColumnName("terminal_id").HasMaxLength(64);
        builder.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
        builder.Property(x => x.RecordUuid).HasColumnName("record_uuid").HasMaxLength(80);
        builder.Property(x => x.RemoteAddress).HasColumnName("remote_address").HasMaxLength(80);
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(1000);
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => x.TerminalId);
    }
}
