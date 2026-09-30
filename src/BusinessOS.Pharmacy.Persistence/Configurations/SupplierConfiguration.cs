using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessOS.Pharmacy.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<SupplierEntity>
{
    public void Configure(EntityTypeBuilder<SupplierEntity> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).ValueGeneratedNever();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).UseCollation("NOCASE").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.ContactPerson).HasColumnName("contact_person").HasMaxLength(160);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(64);
        builder.Property(x => x.Whatsapp).HasColumnName("whatsapp").HasMaxLength(64);
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(255);
        builder.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
        builder.Property(x => x.Province).HasColumnName("province").HasMaxLength(100);
        builder.Property(x => x.PaymentTermsDays).HasColumnName("payment_terms_days").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.IsActive);
    }
}
