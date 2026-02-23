using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class BoardingPassConfiguration : IEntityTypeConfiguration<BoardingPass>
{
    public void Configure(EntityTypeBuilder<BoardingPass> builder)
    {
        builder.HasKey(b => b.BoardingPassId);
        builder.Property(b => b.TicketNumber).HasMaxLength(50).IsRequired();
        builder.Property(b => b.PassengerName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.SeatNumber).HasMaxLength(10);
        builder.Property(b => b.Class).HasMaxLength(20);
        builder.Property(b => b.Gate).HasMaxLength(10);
        builder.Property(b => b.Terminal).HasMaxLength(10);
        builder.Property(b => b.BarcodeData).HasMaxLength(500);
        builder.Property(b => b.QrCodePath).HasMaxLength(500);

        builder.HasOne(b => b.Order)
            .WithMany(o => o.BoardingPasses)
            .HasForeignKey(b => b.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Flight)
            .WithMany(f => f.BoardingPasses)
            .HasForeignKey(b => b.FlightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Companion)
            .WithMany(c => c.BoardingPasses)
            .HasForeignKey(b => b.CompanionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Customer)
            .WithMany(c => c.BoardingPasses)
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
