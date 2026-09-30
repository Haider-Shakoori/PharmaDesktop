using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BusinessOS.Pharmacy.Persistence.Configurations;
internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLineEntity>
{
    public void Configure(EntityTypeBuilder<JournalLineEntity> b)
    {
        b.ToTable("journal_lines"); b.HasKey(x=>x.Id);
        b.Property(x=>x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever(); b.Property(x=>x.JournalEntryId).HasColumnName("journal_entry_id").HasMaxLength(36).IsRequired();
        b.Property(x=>x.LedgerAccountId).HasColumnName("ledger_account_id").HasMaxLength(36).IsRequired(); b.Property(x=>x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36);
        b.Property(x=>x.Debit).HasColumnName("debit").HasPrecision(20,4).IsRequired(); b.Property(x=>x.Credit).HasColumnName("credit").HasPrecision(20,4).IsRequired();
        b.Property(x=>x.CounterpartyType).HasColumnName("counterparty_type").HasMaxLength(80); b.Property(x=>x.CounterpartyId).HasColumnName("counterparty_id").HasMaxLength(64); b.Property(x=>x.Memo).HasColumnName("memo").HasMaxLength(500);
        b.Property(x=>x.CreatedAt).HasColumnName("created_at").IsRequired(); b.Property(x=>x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.HasIndex(x=>new{x.LedgerAccountId,x.JournalEntryId}); b.HasIndex(x=>new{x.CounterpartyType,x.CounterpartyId}); b.HasIndex(x=>new{x.StockLocationId,x.LedgerAccountId});
        b.HasOne(x=>x.JournalEntry).WithMany(x=>x.Lines).HasForeignKey(x=>x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x=>x.LedgerAccount).WithMany().HasForeignKey(x=>x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockLocationEntity>().WithMany().HasForeignKey(x=>x.StockLocationId).OnDelete(DeleteBehavior.SetNull);
    }
}
