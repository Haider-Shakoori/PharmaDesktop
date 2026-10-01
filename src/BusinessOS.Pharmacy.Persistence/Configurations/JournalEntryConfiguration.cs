using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BusinessOS.Pharmacy.Persistence.Configurations;
internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntryEntity>
{
    public void Configure(EntityTypeBuilder<JournalEntryEntity> b)
    {
        b.ToTable("journal_entries"); b.HasKey(x=>x.Id);
        b.Property(x=>x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        b.Property(x=>x.JournalNumber).HasColumnName("journal_number").HasMaxLength(80).IsRequired();
        b.Property(x=>x.BusinessDate).HasColumnName("business_date").IsRequired(); b.Property(x=>x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(x=>x.Status).HasColumnName("status").HasMaxLength(24).IsRequired(); b.Property(x=>x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x=>x.SourceType).HasColumnName("source_type").HasMaxLength(120).IsRequired(); b.Property(x=>x.SourceId).HasColumnName("source_id").HasMaxLength(64).IsRequired();
        b.Property(x=>x.SourceEvent).HasColumnName("source_event").HasMaxLength(80); b.Property(x=>x.SourceNumber).HasColumnName("source_number").HasMaxLength(120);
        b.Property(x=>x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired(); b.Property(x=>x.Reference).HasColumnName("reference").HasMaxLength(160);
        b.Property(x=>x.Description).HasColumnName("description").HasMaxLength(500).IsRequired(); b.Property(x=>x.TotalDebit).HasColumnName("total_debit").HasPrecision(20,4).IsRequired();
        b.Property(x=>x.TotalCredit).HasColumnName("total_credit").HasPrecision(20,4).IsRequired(); b.Property(x=>x.PostedBy).HasColumnName("posted_by").HasMaxLength(64);
        b.Property(x=>x.ReversalOfId).HasColumnName("reversal_of_id").HasMaxLength(36); b.Property(x=>x.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(2000);
        b.Property(x=>x.PostedAt).HasColumnName("posted_at").IsRequired(); b.Property(x=>x.CreatedAt).HasColumnName("created_at").IsRequired(); b.Property(x=>x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.HasIndex(x=>x.JournalNumber).IsUnique(); b.HasIndex(x=>x.BusinessDate); b.HasIndex(x=>x.OccurredAt); b.HasIndex(x=>x.Status); b.HasIndex(x=>x.SourceType); b.HasIndex(x=>x.SourceId);
        b.HasIndex(x=>x.SourceEvent); b.HasIndex(x=>x.IdempotencyKey).IsUnique(); b.HasIndex(x=>x.PostedAt); b.HasIndex(x=>new{x.SourceType,x.SourceId,x.SourceEvent}); b.HasIndex(x=>new{x.BusinessDate,x.Status});
        b.HasOne(x=>x.ReversalOf).WithMany().HasForeignKey(x=>x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
    }
}
