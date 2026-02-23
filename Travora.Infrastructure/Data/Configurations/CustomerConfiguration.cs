using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.CustomerId);
        builder.Property(c => c.Firstname).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Lastname).HasMaxLength(100).IsRequired();
        builder.Property(c => c.PassportNumber).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Nationality).HasMaxLength(100);
        builder.Property(c => c.Gender).HasMaxLength(10);
        builder.Property(c => c.PhoneNumber).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(512);

        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasIndex(c => c.PassportNumber).IsUnique();
    }
}
