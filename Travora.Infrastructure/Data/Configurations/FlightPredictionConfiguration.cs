using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class FlightPredictionConfiguration : IEntityTypeConfiguration<FlightPrediction>
{
    public void Configure(EntityTypeBuilder<FlightPrediction> builder)
    {
        builder.HasKey(f => f.PredictionId);
        builder.Property(f => f.PredictionConfidenceScore).HasPrecision(5, 2);
        builder.Property(f => f.PredictionAccuracy).HasPrecision(5, 2);
        builder.Property(f => f.PredictionModelVersion).HasMaxLength(50);

        builder.HasOne(f => f.Flight)
            .WithMany(fl => fl.Predictions)
            .HasForeignKey(f => f.FlightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.WeatherSnapshot)
            .WithMany(w => w.FlightPredictions)
            .HasForeignKey(f => f.WeatherSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
