using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CustomsItemInvoiceConfiguration : IEntityTypeConfiguration<CustomsItemInvoice>
{
    public void Configure(EntityTypeBuilder<CustomsItemInvoice> builder)
    {
        builder.HasKey(c => c.CustomsItemInvoiceId);
        builder.Property(c => c.InvoicePath).HasMaxLength(500).IsRequired();

        builder.HasOne(c => c.CustomsItem)
            .WithMany(ci => ci.Invoices)
            .HasForeignKey(c => c.CustomsItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
