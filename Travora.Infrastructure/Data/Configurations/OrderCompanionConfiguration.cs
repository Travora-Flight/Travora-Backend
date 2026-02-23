using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class OrderCompanionConfiguration : IEntityTypeConfiguration<OrderCompanion>
{
    public void Configure(EntityTypeBuilder<OrderCompanion> builder)
    {
        builder.HasKey(oc => oc.OrderCompanionId);
        builder.Property(oc => oc.TicketNumber).HasMaxLength(50);

        builder.HasOne(oc => oc.Order)
            .WithMany(o => o.OrderCompanions)
            .HasForeignKey(oc => oc.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(oc => oc.Companion)
            .WithMany(c => c.OrderCompanions)
            .HasForeignKey(oc => oc.CompanionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
