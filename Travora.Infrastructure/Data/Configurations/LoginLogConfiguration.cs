using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class LoginLogConfiguration : IEntityTypeConfiguration<LoginLog>
{
    public void Configure(EntityTypeBuilder<LoginLog> builder)
    {
        builder.HasKey(l => l.LogId);
        builder.Property(l => l.IpAddress).HasMaxLength(50);
        builder.Property(l => l.UserAgent).HasMaxLength(500);
        builder.Property(l => l.DeviceType).HasMaxLength(50);
        builder.Property(l => l.FailureReason).HasMaxLength(300);
        builder.Property(l => l.SessionToken).HasMaxLength(500);

        builder.HasOne(l => l.Admin)
            .WithMany()
            .HasForeignKey(l => l.AdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Customer)
            .WithMany(c => c.LoginLogs)
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Employee)
            .WithMany(e => e.LoginLogs)
            .HasForeignKey(l => l.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
