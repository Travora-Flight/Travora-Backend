using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.NotificationId);
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(2000).IsRequired();

        builder.HasIndex(n => new { n.UserId, n.UserType });

        builder.HasOne(n => n.Order)
            .WithMany(o => o.Notifications)
            .HasForeignKey(n => n.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Baggage)
            .WithMany(b => b.Notifications)
            .HasForeignKey(n => n.BaggageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
