using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class BaggageTrackingConfiguration : IEntityTypeConfiguration<BaggageTracking>
{
    public void Configure(EntityTypeBuilder<BaggageTracking> builder)
    {
        builder.HasKey(b => b.TrackingId);
        builder.Property(b => b.GpsLatitude).HasPrecision(10, 6);
        builder.Property(b => b.GpsLongitude).HasPrecision(10, 6);

        builder.HasOne(b => b.HandledByEmployee)
            .WithMany(e => e.BaggageTrackings)
            .HasForeignKey(b => b.HandledByEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Baggage)
            .WithMany(bg => bg.BaggageTrackings)
            .HasForeignKey(b => b.BaggageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Checkpoint)
            .WithMany(c => c.BaggageTrackings)
            .HasForeignKey(b => b.CheckpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.TriggeredByScan)
            .WithMany(q => q.TriggeredTrackings)
            .HasForeignKey(b => b.TriggeredByScanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
