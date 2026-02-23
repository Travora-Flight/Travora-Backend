using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class CustomsDeclarationConfiguration : IEntityTypeConfiguration<CustomsDeclaration>
{
    public void Configure(EntityTypeBuilder<CustomsDeclaration> builder)
    {
        builder.HasKey(c => c.CustomsId);
        builder.Property(c => c.TotalDeclaredValue).HasPrecision(18, 2);
        builder.Property(c => c.TotalCustomsFee).HasPrecision(18, 2);
        builder.Property(c => c.Notes).HasMaxLength(1000);

        builder.HasOne(c => c.Order)
            .WithMany(o => o.CustomsDeclarations)
            .HasForeignKey(c => c.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
