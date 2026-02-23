using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class FlightPositionHistoryConfiguration : IEntityTypeConfiguration<FlightPositionHistory>
{
    public void Configure(EntityTypeBuilder<FlightPositionHistory> builder)
    {
        builder.HasKey(f => f.PositionId);
        builder.Property(f => f.Altitude).HasPrecision(12, 2);
        builder.Property(f => f.Direction).HasPrecision(6, 2);
        builder.Property(f => f.HorizontalSpeed).HasPrecision(10, 2);
        builder.Property(f => f.VerticalSpeed).HasPrecision(10, 2);
        builder.Property(f => f.Latitude).HasPrecision(10, 6);
        builder.Property(f => f.Longitude).HasPrecision(10, 6);
        builder.Property(f => f.Squawk).HasMaxLength(10);

        builder.HasOne(f => f.Flight)
            .WithMany(fl => fl.PositionHistory)
            .HasForeignKey(f => f.FlightId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
