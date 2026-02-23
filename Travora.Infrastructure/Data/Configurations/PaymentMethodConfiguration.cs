using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasKey(p => p.PaymentMethodId);
        builder.Property(p => p.CardLastFour).HasMaxLength(4);
        builder.Property(p => p.CardHolderName).HasMaxLength(200);
        builder.Property(p => p.CardBrand).HasMaxLength(50);

        builder.HasOne(p => p.Customer)
            .WithMany(c => c.PaymentMethods)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
