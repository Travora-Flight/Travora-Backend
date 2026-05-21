using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class BaggagePhotoConfiguration : IEntityTypeConfiguration<BaggagePhoto>
{
    public void Configure(EntityTypeBuilder<BaggagePhoto> builder)
    {
        builder.HasKey(b => b.PhotoId);
        builder.Property(b => b.ImagePath).HasMaxLength(500).IsRequired();

        builder.HasOne(b => b.CapturedByEmployee)
            .WithMany(e => e.BaggagePhotos)
            .HasForeignKey(b => b.CapturedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.CapturedByCustomer)
            .WithMany(c => c.BaggagePhotos)
            .HasForeignKey(b => b.CapturedByCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Baggage)
            .WithMany(bg => bg.BaggagePhotos)
            .HasForeignKey(b => b.BaggageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Checkpoint)
            .WithMany(c => c.BaggagePhotos)
            .HasForeignKey(b => b.CheckpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.OrderService)
            .WithMany()
            .HasForeignKey(b => b.OrderServiceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
