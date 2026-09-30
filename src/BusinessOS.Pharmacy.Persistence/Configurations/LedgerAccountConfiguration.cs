using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BusinessOS.Pharmacy.Persistence.Configurations;
internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccountEntity>
{
    public void Configure(EntityTypeBuilder<LedgerAccountEntity> b)
    {
        b.ToTable("ledger_accounts"); b.HasKey(x=>x.Id);
        b.Property(x=>x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        b.Property(x=>x.ParentId).HasColumnName("parent_id").HasMaxLength(36);
        b.Property(x=>x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        b.Property(x=>x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x=>x.Type).HasColumnName("type").HasMaxLength(32).IsRequired();
        b.Property(x=>x.NormalBalance).HasColumnName("normal_balance").HasMaxLength(8).IsRequired();
        b.Property(x=>x.SystemKey).HasColumnName("system_key").HasMaxLength(64);
        b.Property(x=>x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x=>x.IsSystem).HasColumnName("is_system").IsRequired();
        b.Property(x=>x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x=>x.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(x=>x.CreatedAt).HasColumnName("created_at").IsRequired(); b.Property(x=>x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.HasIndex(x=>x.Code).IsUnique(); b.HasIndex(x=>x.Type); b.HasIndex(x=>x.SystemKey).IsUnique(); b.HasIndex(x=>x.IsSystem); b.HasIndex(x=>x.IsActive); b.HasIndex(x=>new{x.Type,x.IsActive});
        b.HasOne(x=>x.Parent).WithMany().HasForeignKey(x=>x.ParentId).OnDelete(DeleteBehavior.SetNull);
    }
}
