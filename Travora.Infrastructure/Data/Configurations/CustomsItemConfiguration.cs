using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CustomsItemConfiguration : IEntityTypeConfiguration<CustomsItem>
{
    public void Configure(EntityTypeBuilder<CustomsItem> builder)
    {
        builder.HasKey(c => c.CustomsItemId);
        builder.Property(c => c.ItemDescription).HasMaxLength(500).IsRequired();
        builder.Property(c => c.DeclaredValue).HasPrecision(18, 2);
        builder.Property(c => c.TotalValue).HasPrecision(18, 2);
        builder.Property(c => c.CustomsRatePercentage).HasPrecision(5, 2);
        builder.Property(c => c.TotalCustomsValue).HasPrecision(18, 2);
        builder.Property(c => c.PurchaseInvoicePath).HasMaxLength(500);
        builder.Property(c => c.ExternalCategoryId).HasMaxLength(100);
        builder.Property(c => c.ExternalCategoryName).HasMaxLength(200);

        builder.HasOne(c => c.CustomsDeclaration)
            .WithMany(cd => cd.CustomsItems)
            .HasForeignKey(c => c.CustomsId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
