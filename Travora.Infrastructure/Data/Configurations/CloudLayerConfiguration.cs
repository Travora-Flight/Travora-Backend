using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CloudLayerConfiguration : IEntityTypeConfiguration<CloudLayer>
{
    public void Configure(EntityTypeBuilder<CloudLayer> builder)
    {
        builder.HasKey(c => c.CloudLayerId);
        builder.Property(c => c.CoverType).HasMaxLength(50);

        builder.HasOne(c => c.WeatherSnapshot)
            .WithMany(w => w.CloudLayers)
            .HasForeignKey(c => c.WeatherSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
