using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CustomerCompanionConfiguration : IEntityTypeConfiguration<CustomerCompanion>
{
    public void Configure(EntityTypeBuilder<CustomerCompanion> builder)
    {
        builder.HasKey(cc => cc.RelationId);

        builder.HasOne(cc => cc.Customer)
            .WithMany(c => c.CustomerCompanions)
            .HasForeignKey(cc => cc.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cc => cc.Companion)
            .WithMany(c => c.CustomerCompanions)
            .HasForeignKey(cc => cc.CompanionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cc => new { cc.CustomerId, cc.CompanionId }).IsUnique();
    }
}
