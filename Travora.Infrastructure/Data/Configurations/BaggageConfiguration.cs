using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class BaggageConfiguration : IEntityTypeConfiguration<Baggage>
{
    public void Configure(EntityTypeBuilder<Baggage> builder)
    {
        builder.HasKey(b => b.BaggageId);
        builder.Property(b => b.Description).HasMaxLength(500);

        builder.HasOne(b => b.Order)
            .WithMany(o => o.Baggages)
            .HasForeignKey(b => b.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Customer)
            .WithMany(c => c.Baggages)
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Companion)
            .WithMany(c => c.Baggages)
            .HasForeignKey(b => b.CompanionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
