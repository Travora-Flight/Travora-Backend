using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.HasKey(f => f.FeedbackId);
        builder.Property(f => f.Comment).HasMaxLength(2000);

        builder.HasOne(f => f.Order)
            .WithMany(o => o.Feedbacks)
            .HasForeignKey(f => f.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Customer)
            .WithMany(c => c.Feedbacks)
            .HasForeignKey(f => f.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
