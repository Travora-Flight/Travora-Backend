using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class WeatherSnapshotConfiguration : IEntityTypeConfiguration<WeatherSnapshot>
{
    public void Configure(EntityTypeBuilder<WeatherSnapshot> builder)
    {
        builder.HasKey(w => w.WeatherSnapshotId);
        builder.Property(w => w.IcaoId).HasMaxLength(5).IsRequired();
        builder.Property(w => w.Temperature).HasPrecision(6, 2);
        builder.Property(w => w.Dewpoint).HasPrecision(6, 2);
        builder.Property(w => w.WindSpeed).HasPrecision(6, 2);
        builder.Property(w => w.Visibility).HasMaxLength(20);
        builder.Property(w => w.Altimeter).HasPrecision(8, 2);
        builder.Property(w => w.MetarType).HasMaxLength(10);
        builder.Property(w => w.RawObservation).HasMaxLength(500);
        builder.Property(w => w.CloudCover).HasMaxLength(20);

        builder.HasIndex(w => w.IcaoId);

        builder.HasOne(w => w.Airport)
            .WithMany(a => a.WeatherSnapshots)
            .HasForeignKey(w => w.IcaoId)
            .HasPrincipalKey(a => a.CodeIcaoAirport)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
