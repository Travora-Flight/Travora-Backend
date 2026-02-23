using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.DocumentId);
        builder.Property(d => d.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(d => d.MimeType).HasMaxLength(50);

        builder.HasOne(d => d.VerifiedByAdmin)
            .WithMany(a => a.VerifiedDocuments)
            .HasForeignKey(d => d.VerifiedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.ReplacedByDocument)
            .WithMany()
            .HasForeignKey(d => d.ReplacedByDocumentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
