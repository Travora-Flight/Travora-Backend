using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class DriverTrackingConfiguration : IEntityTypeConfiguration<DriverTracking>
{
    public void Configure(EntityTypeBuilder<DriverTracking> builder)
    {
        builder.HasKey(d => d.TrackingId);
        builder.Property(d => d.GpsLatitude).HasPrecision(10, 6);
        builder.Property(d => d.GpsLongitude).HasPrecision(10, 6);
        builder.Property(d => d.AccuracyMeters).HasPrecision(10, 2);
        builder.Property(d => d.SpeedKmh).HasPrecision(8, 2);
        builder.Property(d => d.HeadingDegrees).HasPrecision(6, 2);

        builder.HasOne(d => d.Driver)
            .WithMany(e => e.DriverTrackings)
            .HasForeignKey(d => d.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.OrderService)
            .WithMany(os => os.DriverTrackings)
            .HasForeignKey(d => d.OrderServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
