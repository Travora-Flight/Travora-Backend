using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class SavedFlightConfiguration : IEntityTypeConfiguration<SavedFlight>
{
    public void Configure(EntityTypeBuilder<SavedFlight> builder)
    {
        builder.HasKey(s => s.SavedFlightId);

        builder.HasOne(s => s.Customer)
            .WithMany(c => c.SavedFlights)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Flight)
            .WithMany(f => f.SavedFlights)
            .HasForeignKey(s => s.FlightId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CustomerId, s.FlightId }).HasFilter("[CustomerId] IS NOT NULL").IsUnique();
        builder.HasIndex(s => new { s.GuestId, s.FlightId }).HasFilter("[GuestId] IS NOT NULL").IsUnique();
    }
}
