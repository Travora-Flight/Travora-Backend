using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Data.Configurations;

public class OrderServiceConfiguration : IEntityTypeConfiguration<OrderService>
{
    public void Configure(EntityTypeBuilder<OrderService> builder)
    {
        builder.HasKey(os => os.OrderServiceId);
        builder.Property(os => os.ServiceFee).HasPrecision(18, 2);

        builder.HasOne(os => os.Order)
            .WithMany(o => o.OrderServices)
            .HasForeignKey(os => os.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(os => os.PackageService)
            .WithMany(ps => ps.OrderServices)
            .HasForeignKey(os => os.PackageServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(os => os.AssignedEmployee)
            .WithMany(e => e.AssignedOrderServices)
            .HasForeignKey(os => os.AssignedEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
