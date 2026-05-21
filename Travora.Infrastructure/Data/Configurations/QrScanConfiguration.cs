using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class QrScanConfiguration : IEntityTypeConfiguration<QrScan>
{
    public void Configure(EntityTypeBuilder<QrScan> builder)
    {
        builder.HasKey(q => q.ScanId);
        builder.Property(q => q.GpsLatitude).HasPrecision(10, 6);
        builder.Property(q => q.GpsLongitude).HasPrecision(10, 6);
        builder.Property(q => q.Description).HasMaxLength(500);

        builder.HasOne(q => q.Baggage)
            .WithMany(b => b.QrScans)
            .HasForeignKey(q => q.BaggageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.Checkpoint)
            .WithMany(c => c.QrScans)
            .HasForeignKey(q => q.CheckpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.ScannedByEmployee)
            .WithMany(e => e.QrScans)
            .HasForeignKey(q => q.ScannedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.ScannedByCustomer)
            .WithMany(c => c.QrScans)
            .HasForeignKey(q => q.ScannedByCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.OrderService)
            .WithMany()
            .HasForeignKey(q => q.OrderServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
