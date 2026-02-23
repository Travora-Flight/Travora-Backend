using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CompanionConfiguration : IEntityTypeConfiguration<Companion>
{
    public void Configure(EntityTypeBuilder<Companion> builder)
    {
        builder.HasKey(c => c.CompanionId);
        builder.Property(c => c.Firstname).HasMaxLength(100);
        builder.Property(c => c.Lastname).HasMaxLength(100);
        builder.Property(c => c.PassportNumber).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Nationality).HasMaxLength(100);

        builder.HasIndex(c => c.PassportNumber);
    }
}
