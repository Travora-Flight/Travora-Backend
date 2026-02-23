using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.HasKey(s => s.ServiceId);
        builder.Property(s => s.ServiceCode).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ServiceName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.BasePrice).HasPrecision(18, 2);

        builder.HasIndex(s => s.ServiceCode).IsUnique();
    }
}
