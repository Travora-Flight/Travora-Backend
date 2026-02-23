using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.HasKey(p => p.PackageId);
        builder.Property(p => p.PackageCode).HasMaxLength(20).IsRequired();
        builder.Property(p => p.PackageName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.TotalBasePrice).HasPrecision(18, 2);
        builder.Property(p => p.ExtraCompanionPrice).HasPrecision(18, 2);
        builder.Property(p => p.ExtraBaggagePrice).HasPrecision(18, 2);
        builder.Property(p => p.Discount).HasPrecision(5, 2);

        builder.HasIndex(p => p.PackageCode).IsUnique();
    }
}
