using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.HasKey(r => r.ReportId);
        builder.Property(r => r.ReportName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ReportFilePath).HasMaxLength(500);

        builder.HasOne(r => r.GeneratedByAdmin)
            .WithMany(a => a.GeneratedReports)
            .HasForeignKey(r => r.GeneratedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
