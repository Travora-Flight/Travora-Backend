using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.ExtraCompanionsFee).HasPrecision(18, 2);
        builder.Property(o => o.ExtraBaggageFee).HasPrecision(18, 2);
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Property(o => o.SpecialInstructions).HasMaxLength(1000);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.PickupTimeSlot).HasMaxLength(50);
        builder.Property(o => o.DeliveryTimeSlot).HasMaxLength(50);
        builder.Property(o => o.Comment).HasMaxLength(1000);

        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Flight)
            .WithMany(f => f.Orders)
            .HasForeignKey(o => o.FlightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Package)
            .WithMany(p => p.Orders)
            .HasForeignKey(o => o.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.PickupLocation)
            .WithMany(l => l.PickupOrders)
            .HasForeignKey(o => o.PickupLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.DeliveryLocation)
            .WithMany(l => l.DeliveryOrders)
            .HasForeignKey(o => o.DeliveryLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
