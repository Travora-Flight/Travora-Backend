using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.HasKey(s => s.SettingsId);
        
        builder.Property(s => s.CompanyName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.CompanyEmail).HasMaxLength(150).IsRequired();
        builder.Property(s => s.CompanyPhone).HasMaxLength(50);
        builder.Property(s => s.CompanyAddress).HasMaxLength(300);
        builder.Property(s => s.Timezone).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Language).HasMaxLength(50).IsRequired();
    }
}
