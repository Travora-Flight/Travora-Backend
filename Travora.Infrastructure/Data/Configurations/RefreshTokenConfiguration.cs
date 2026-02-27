using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(r => r.TokenId);
        builder.Property(r => r.Token).HasMaxLength(500).IsRequired();
        builder.HasIndex(r => r.Token).IsUnique();
        builder.HasIndex(r => r.UserId);
    }
}
