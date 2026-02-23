using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.HasKey(v => v.VehicleId);
        builder.Property(v => v.PlateNumber).HasMaxLength(20).IsRequired();
        builder.Property(v => v.Brand).HasMaxLength(50);
        builder.Property(v => v.Model).HasMaxLength(50);
        builder.Property(v => v.Color).HasMaxLength(30);

        builder.HasIndex(v => v.PlateNumber).IsUnique();
    }
}
