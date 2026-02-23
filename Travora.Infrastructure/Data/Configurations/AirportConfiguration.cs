using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class AirportConfiguration : IEntityTypeConfiguration<Airport>
{
    public void Configure(EntityTypeBuilder<Airport> builder)
    {
        builder.HasKey(a => a.AirportId);
        builder.Property(a => a.NameAirport).HasMaxLength(200).IsRequired();
        builder.Property(a => a.CodeIataAirport).HasMaxLength(5);
        builder.Property(a => a.CodeIcaoAirport).HasMaxLength(5);
        builder.Property(a => a.CodeIataCity).HasMaxLength(5);
        builder.Property(a => a.CodeIso2Country).HasMaxLength(5);
        builder.Property(a => a.LatitudeAirport).HasPrecision(10, 6);
        builder.Property(a => a.LongitudeAirport).HasPrecision(10, 6);
        builder.Property(a => a.GMT).HasMaxLength(10);
        builder.Property(a => a.GeonameId).HasMaxLength(20);
        builder.Property(a => a.Phone).HasMaxLength(30);

        builder.HasIndex(a => a.CodeIataAirport).IsUnique();
        builder.HasIndex(a => a.CodeIcaoAirport);

        builder.HasOne(a => a.City)
            .WithMany(c => c.Airports)
            .HasForeignKey(a => a.CodeIataCity)
            .HasPrincipalKey(c => c.CodeIataCity)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Country)
            .WithMany(c => c.Airports)
            .HasForeignKey(a => a.CodeIso2Country)
            .HasPrincipalKey(c => c.Iso2Code)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
