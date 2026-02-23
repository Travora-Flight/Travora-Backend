using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.HasKey(c => c.CityId);
        builder.Property(c => c.NameCity).HasMaxLength(100).IsRequired();
        builder.Property(c => c.CodeIataCity).HasMaxLength(5);
        builder.Property(c => c.CodeIso2Country).HasMaxLength(5);
        builder.Property(c => c.LatitudeCity).HasPrecision(10, 6);
        builder.Property(c => c.LongitudeCity).HasPrecision(10, 6);
        builder.Property(c => c.Timezone).HasMaxLength(50);
        builder.Property(c => c.GMT).HasMaxLength(10);

        builder.HasIndex(c => c.CodeIataCity).IsUnique();

        builder.HasOne(c => c.Country)
            .WithMany(co => co.Cities)
            .HasForeignKey(c => c.CodeIso2Country)
            .HasPrincipalKey(co => co.Iso2Code)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
