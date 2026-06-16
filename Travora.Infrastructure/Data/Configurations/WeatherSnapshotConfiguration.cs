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
        builder.Property(w => w.FeelsLike).HasPrecision(6, 2);
        builder.Property(w => w.WindSpeed).HasPrecision(6, 2);
        builder.Property(w => w.Visibility).HasMaxLength(20);
        builder.Property(w => w.Altimeter).HasPrecision(8, 2); // pressure_mb
        builder.Property(w => w.Humidity).IsRequired();
        
        builder.Property(w => w.ConditionText).HasMaxLength(100).IsRequired();
        builder.Property(w => w.ConditionIcon).HasMaxLength(200).IsRequired();
        builder.Property(w => w.ConditionCode).IsRequired();

        builder.Property(w => w.Sunrise).HasMaxLength(20).IsRequired();
        builder.Property(w => w.Sunset).HasMaxLength(20).IsRequired();
        builder.Property(w => w.ChanceOfRain).IsRequired();
        builder.Property(w => w.MaxTemp).HasPrecision(6, 2);
        builder.Property(w => w.MinTemp).HasPrecision(6, 2);

        builder.HasIndex(w => w.IcaoId);

        builder.HasOne(w => w.Airport)
            .WithMany(a => a.WeatherSnapshots)
            .HasForeignKey(w => w.IcaoId)
            .HasPrincipalKey(a => a.CodeIcaoAirport)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
