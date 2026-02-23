using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class FlightConfiguration : IEntityTypeConfiguration<Flight>
{
    public void Configure(EntityTypeBuilder<Flight> builder)
    {
        builder.HasKey(f => f.FlightId);
        builder.Property(f => f.FlightNumber).HasMaxLength(20);
        builder.Property(f => f.FlightIataNumber).HasMaxLength(10);
        builder.Property(f => f.FlightIcaoNumber).HasMaxLength(10);
        builder.Property(f => f.FlightType).HasMaxLength(20);
        builder.Property(f => f.DepartureIataCode).HasMaxLength(5);
        builder.Property(f => f.DepartureIcaoCode).HasMaxLength(5);
        builder.Property(f => f.DepartureTerminal).HasMaxLength(10);
        builder.Property(f => f.DepartureGate).HasMaxLength(10);
        builder.Property(f => f.DepartureBaggage).HasMaxLength(10);
        builder.Property(f => f.ArrivalIataCode).HasMaxLength(5);
        builder.Property(f => f.ArrivalIcaoCode).HasMaxLength(5);
        builder.Property(f => f.ArrivalTerminal).HasMaxLength(10);
        builder.Property(f => f.ArrivalGate).HasMaxLength(10);
        builder.Property(f => f.ArrivalBaggage).HasMaxLength(10);
        builder.Property(f => f.AirlineName).HasMaxLength(200);
        builder.Property(f => f.AirlineIataCode).HasMaxLength(5);
        builder.Property(f => f.AirlineIcaoCode).HasMaxLength(5);
        builder.Property(f => f.AircraftModelCode).HasMaxLength(20);
        builder.Property(f => f.AircraftModelText).HasMaxLength(100);
        builder.Property(f => f.AircraftRegistrationNumber).HasMaxLength(50);
        builder.Property(f => f.Weekday).HasMaxLength(10);
        builder.Property(f => f.DataSource).HasMaxLength(50);

        builder.HasIndex(f => f.FlightIataNumber);
        builder.HasIndex(f => f.FlightIcaoNumber);
        builder.HasIndex(f => f.DepartureIataCode);
        builder.HasIndex(f => f.ArrivalIataCode);

        builder.HasOne(f => f.Airline)
            .WithMany(a => a.Flights)
            .HasForeignKey(f => f.AirlineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.DepartureAirport)
            .WithMany(a => a.DepartureFlights)
            .HasForeignKey(f => f.DepartureAirportId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.ArrivalAirport)
            .WithMany(a => a.ArrivalFlights)
            .HasForeignKey(f => f.ArrivalAirportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
