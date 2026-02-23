using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class AircraftConfiguration : IEntityTypeConfiguration<Aircraft>
{
    public void Configure(EntityTypeBuilder<Aircraft> builder)
    {
        builder.HasKey(a => a.AirplaneId);
        builder.Property(a => a.NumberRegistration).HasMaxLength(50);
        builder.Property(a => a.HexIcaoAirplane).HasMaxLength(20);
        builder.Property(a => a.AirplaneIataType).HasMaxLength(10);
        builder.Property(a => a.CodeIataPlaneLong).HasMaxLength(20);
        builder.Property(a => a.CodeIataPlaneShort).HasMaxLength(10);
        builder.Property(a => a.CodeIataAirline).HasMaxLength(10);
        builder.Property(a => a.CodeIcaoAirline).HasMaxLength(10);
        builder.Property(a => a.ConstructionNumber).HasMaxLength(50);
        builder.Property(a => a.LineNumber).HasMaxLength(20);
        builder.Property(a => a.ModelCode).HasMaxLength(20);
        builder.Property(a => a.EnginesType).HasMaxLength(50);
        builder.Property(a => a.PlaneClass).HasMaxLength(50);
        builder.Property(a => a.PlaneModel).HasMaxLength(100);
        builder.Property(a => a.PlaneSeries).HasMaxLength(100);
        builder.Property(a => a.PlaneOwner).HasMaxLength(200);
        builder.Property(a => a.PlaneStatus).HasMaxLength(20);
        builder.Property(a => a.ProductionLine).HasMaxLength(100);
        builder.Property(a => a.NumberTestRegistration).HasMaxLength(50);

        builder.HasOne(a => a.Airline)
            .WithMany(al => al.Aircrafts)
            .HasForeignKey(a => a.AirlineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
