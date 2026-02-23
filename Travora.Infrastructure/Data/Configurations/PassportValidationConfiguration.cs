using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class PassportValidationConfiguration : IEntityTypeConfiguration<PassportValidation>
{
    public void Configure(EntityTypeBuilder<PassportValidation> builder)
    {
        builder.HasKey(p => p.ValidationId);
        builder.Property(p => p.OcrConfidenceScore).HasPrecision(5, 2);

        builder.HasOne(p => p.Document)
            .WithMany(d => d.PassportValidations)
            .HasForeignKey(p => p.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
