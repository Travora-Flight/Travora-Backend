using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CheckpointConfiguration : IEntityTypeConfiguration<Checkpoint>
{
    public void Configure(EntityTypeBuilder<Checkpoint> builder)
    {
        builder.HasKey(c => c.CheckpointId);
        builder.Property(c => c.CheckpointName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.GpsLatitude).HasPrecision(10, 6);
        builder.Property(c => c.GpsLongitude).HasPrecision(10, 6);

        builder.HasOne(c => c.Airport)
            .WithMany(a => a.Checkpoints)
            .HasForeignKey(c => c.AirportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
