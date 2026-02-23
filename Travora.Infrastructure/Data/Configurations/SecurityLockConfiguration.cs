using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class SecurityLockConfiguration : IEntityTypeConfiguration<SecurityLock>
{
    public void Configure(EntityTypeBuilder<SecurityLock> builder)
    {
        builder.HasKey(s => s.LockId);
        builder.Property(s => s.LockCode).HasMaxLength(50).IsRequired();

        builder.HasOne(s => s.AppliedByEmployee)
            .WithMany(e => e.AppliedSecurityLocks)
            .HasForeignKey(s => s.AppliedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Baggage)
            .WithMany(b => b.SecurityLocks)
            .HasForeignKey(s => s.BaggageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
