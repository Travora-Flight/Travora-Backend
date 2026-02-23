using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(l => l.LocationId);
        builder.Property(l => l.StreetAddress).HasMaxLength(300);
        builder.Property(l => l.Apartment).HasMaxLength(50);
        builder.Property(l => l.City).HasMaxLength(100);
        builder.Property(l => l.State).HasMaxLength(100);
        builder.Property(l => l.Country).HasMaxLength(100);
        builder.Property(l => l.PostalCode).HasMaxLength(20);
        builder.Property(l => l.GpsLatitude).HasPrecision(10, 6);
        builder.Property(l => l.GpsLongitude).HasPrecision(10, 6);

        builder.HasOne(l => l.Customer)
            .WithMany(c => c.Locations)
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
