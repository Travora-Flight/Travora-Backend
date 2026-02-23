using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class AirlineConfiguration : IEntityTypeConfiguration<Airline>
{
    public void Configure(EntityTypeBuilder<Airline> builder)
    {
        builder.HasKey(a => a.AirlineId);
        builder.Property(a => a.NameAirline).HasMaxLength(200).IsRequired();
        builder.Property(a => a.CodeIataAirline).HasMaxLength(5);
        builder.Property(a => a.CodeIcaoAirline).HasMaxLength(5);
        builder.Property(a => a.NameCountry).HasMaxLength(100);
        builder.Property(a => a.CodeIso2Country).HasMaxLength(5);
        builder.Property(a => a.Callsign).HasMaxLength(100);
        builder.Property(a => a.CodeHub).HasMaxLength(5);
        builder.Property(a => a.IataPrefixAccounting).HasMaxLength(10);
        builder.Property(a => a.Type).HasMaxLength(50);
        builder.Property(a => a.StatusAirline).HasMaxLength(20);
        builder.Property(a => a.LogoUrl).HasMaxLength(500);
        builder.Property(a => a.AgeFleet).HasPrecision(5, 1);

        builder.HasIndex(a => a.CodeIataAirline);
        builder.HasIndex(a => a.CodeIcaoAirline);

        builder.HasOne(a => a.Country)
            .WithMany(c => c.Airlines)
            .HasForeignKey(a => a.CodeIso2Country)
            .HasPrincipalKey(c => c.Iso2Code)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.HubAirport)
            .WithMany()
            .HasForeignKey(a => a.CodeHub)
            .HasPrincipalKey(ap => ap.CodeIataAirport)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
