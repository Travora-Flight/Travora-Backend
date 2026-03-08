using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.HasKey(c => c.CountryId);
        builder.Property(c => c.CountryName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Iso2Code).HasMaxLength(5).IsRequired();
        builder.Property(c => c.Iso3Code).HasMaxLength(5);
        builder.Property(c => c.NumericIso).HasMaxLength(5);
        builder.Property(c => c.Continent).HasMaxLength(20);
        builder.Property(c => c.Capital).HasMaxLength(100);
        builder.Property(c => c.CurrencyCode).HasMaxLength(5);
        builder.Property(c => c.CurrencyName).HasMaxLength(50);
        builder.Property(c => c.PhonePrefix).HasMaxLength(100);
        builder.Property(c => c.FipsCode).HasMaxLength(10);

        builder.HasIndex(c => c.Iso2Code).IsUnique();
        builder.HasIndex(c => c.Iso3Code);
    }
}
