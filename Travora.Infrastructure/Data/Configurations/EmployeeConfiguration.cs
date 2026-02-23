using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(e => e.EmployeeId);
        builder.Property(e => e.Firstname).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Lastname).HasMaxLength(100).IsRequired();
        builder.Property(e => e.PhoneNumber).HasMaxLength(20);
        builder.Property(e => e.Email).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PasswordHash).HasMaxLength(512);
        builder.Property(e => e.TempPassword).HasMaxLength(512);
        builder.Property(e => e.NationalId).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ProfileImagePath).HasMaxLength(500);
        builder.Property(e => e.DriverLicensePath).HasMaxLength(500);

        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasIndex(e => e.NationalId).IsUnique();

        builder.HasOne(e => e.CreatedByAdmin)
            .WithMany(a => a.CreatedEmployees)
            .HasForeignKey(e => e.CreatedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Checkpoint)
            .WithMany(c => c.Employees)
            .HasForeignKey(e => e.CheckpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vehicle)
            .WithMany(v => v.Employees)
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
