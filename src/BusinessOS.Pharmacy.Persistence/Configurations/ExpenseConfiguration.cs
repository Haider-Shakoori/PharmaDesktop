using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace BusinessOS.Pharmacy.Persistence.Configurations;
internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<ExpenseEntity>
{
    public void Configure(EntityTypeBuilder<ExpenseEntity> b)
    {
        b.ToTable("expenses"); b.HasKey(x=>x.Id);
        b.Property(x=>x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever(); b.Property(x=>x.ExpenseNumber).HasColumnName("expense_number").HasMaxLength(80).IsRequired();
        b.Property(x=>x.ExpenseAccountId).HasColumnName("expense_account_id").HasMaxLength(36).IsRequired(); b.Property(x=>x.PaymentAccountId).HasColumnName("payment_account_id").HasMaxLength(36).IsRequired();
        b.Property(x=>x.StockLocationId).HasColumnName("stock_location_id").HasMaxLength(36); b.Property(x=>x.BusinessDate).HasColumnName("business_date").IsRequired(); b.Property(x=>x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x=>x.Amount).HasColumnName("amount").HasPrecision(20,4).IsRequired(); b.Property(x=>x.Payee).HasColumnName("payee").HasMaxLength(180); b.Property(x=>x.Reference).HasColumnName("reference").HasMaxLength(160);
        b.Property(x=>x.Notes).HasColumnName("notes").HasMaxLength(2000); b.Property(x=>x.Status).HasColumnName("status").HasMaxLength(24).IsRequired(); b.Property(x=>x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(191).IsRequired();
        b.Property(x=>x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired(); b.Property(x=>x.PostedAt).HasColumnName("posted_at").IsRequired(); b.Property(x=>x.ReversedAt).HasColumnName("reversed_at");
        b.Property(x=>x.CreatedAt).HasColumnName("created_at").IsRequired(); b.Property(x=>x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.HasIndex(x=>x.ExpenseNumber).IsUnique(); b.HasIndex(x=>x.BusinessDate); b.HasIndex(x=>x.Status); b.HasIndex(x=>x.IdempotencyKey).IsUnique(); b.HasIndex(x=>x.PostedAt); b.HasIndex(x=>x.ReversedAt);
        b.HasOne(x=>x.ExpenseAccount).WithMany().HasForeignKey(x=>x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x=>x.PaymentAccount).WithMany().HasForeignKey(x=>x.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.StockLocation).WithMany().HasForeignKey(x=>x.StockLocationId).OnDelete(DeleteBehavior.SetNull);
    }
}
