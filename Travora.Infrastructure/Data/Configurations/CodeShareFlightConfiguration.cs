using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CodeShareFlightConfiguration : IEntityTypeConfiguration<CodeShareFlight>
{
    public void Configure(EntityTypeBuilder<CodeShareFlight> builder)
    {
        builder.HasKey(c => c.CodeShareId);
        builder.Property(c => c.MarketingAirlineName).HasMaxLength(200);
        builder.Property(c => c.MarketingFlightNumber).HasMaxLength(20);
        builder.Property(c => c.MarketingIataNumber).HasMaxLength(10);
        builder.Property(c => c.MarketingIcaoNumber).HasMaxLength(10);

        builder.HasOne(c => c.OperatingFlight)
            .WithMany(f => f.CodeShareFlights)
            .HasForeignKey(c => c.OperatingFlightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.MarketingAirline)
            .WithMany(a => a.MarketingFlights)
            .HasForeignKey(c => c.MarketingAirlineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
