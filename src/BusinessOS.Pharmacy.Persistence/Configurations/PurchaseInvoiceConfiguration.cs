using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoiceEntity>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceEntity> builder)
    {
        builder.ToTable("purchase_invoices");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").HasMaxLength(36).IsRequired();
        builder.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasMaxLength(36);
        builder.Property(x => x.GoodsReceiptId).HasColumnName("goods_receipt_id").HasMaxLength(36);
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.SupplierInvoiceNumber).HasColumnName("supplier_invoice_number").HasMaxLength(120);
        builder.Property(x => x.InvoiceDate).HasColumnName("invoice_date").IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.DiscountTotal).HasColumnName("discount_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.LandedCostTotal).HasColumnName("landed_cost_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.GrandTotal).HasColumnName("grand_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.PaidTotal).HasColumnName("paid_total").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.BalanceDue).HasColumnName("balance_due").HasPrecision(20, 4).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => x.InvoiceDate);
        builder.HasIndex(x => x.DueDate);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.SupplierId, x.SupplierInvoiceNumber }).IsUnique();
        builder.HasIndex(x => new { x.SupplierId, x.InvoiceDate });

        builder.HasOne(x => x.Supplier)
            .WithMany(x => x.Invoices)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PurchaseOrder)
            .WithMany(x => x.Invoices)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.GoodsReceipt)
            .WithMany(x => x.Invoices)
            .HasForeignKey(x => x.GoodsReceiptId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
