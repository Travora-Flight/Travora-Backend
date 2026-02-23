using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class AdminConfiguration : IEntityTypeConfiguration<Admin>
{
    public void Configure(EntityTypeBuilder<Admin> builder)
    {
        builder.HasKey(a => a.AdminId);
        builder.Property(a => a.Username).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(a => a.FullName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.PhoneNumber).HasMaxLength(20);

        builder.HasIndex(a => a.Email).IsUnique();
        builder.HasIndex(a => a.Username).IsUnique();
    }
}
