using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShiftEnumValuesToOneBased : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert all Enum values from 0-based to 1-based
            // Note: BaggageTrackingStatus and CheckpointType were already 1-based, so no update needed

            // Orders
            migrationBuilder.Sql("UPDATE Orders SET OrderStatus = OrderStatus + 1");
            // Flights
            migrationBuilder.Sql("UPDATE Flights SET FlightStatus = FlightStatus + 1");
            // BoardingPasses
            migrationBuilder.Sql("UPDATE BoardingPasses SET BoardingStatus = BoardingStatus + 1");
            // Customers
            migrationBuilder.Sql("UPDATE Customers SET AccountStatus = AccountStatus + 1");
            // LoginLogs
            migrationBuilder.Sql("UPDATE LoginLogs SET LoginStatus = LoginStatus + 1");
            migrationBuilder.Sql("UPDATE LoginLogs SET UserType = UserType + 1");
            // Payments
            migrationBuilder.Sql("UPDATE Payments SET PaymentStatus = PaymentStatus + 1");
            // Invoices
            migrationBuilder.Sql("UPDATE Invoices SET InvoiceStatus = InvoiceStatus + 1");
            // Refunds
            migrationBuilder.Sql("UPDATE Refunds SET RefundStatus = RefundStatus + 1");
            // OrderServices
            migrationBuilder.Sql("UPDATE OrderServices SET ServiceStatus = ServiceStatus + 1");
            // Services
            migrationBuilder.Sql("UPDATE Services SET ServiceType = ServiceType + 1");
            // Employees
            migrationBuilder.Sql("UPDATE Employees SET JobRole = JobRole + 1");
            migrationBuilder.Sql("UPDATE Employees SET ShiftType = ShiftType + 1");
            // RefreshTokens
            migrationBuilder.Sql("UPDATE RefreshTokens SET UserType = UserType + 1");
            // PaymentMethods
            migrationBuilder.Sql("UPDATE PaymentMethods SET PaymentFunding = PaymentFunding + 1");
            // PassportValidations
            migrationBuilder.Sql("UPDATE PassportValidations SET ValidationStatus = ValidationStatus + 1");
            // PackageServices
            migrationBuilder.Sql("UPDATE PackageServices SET ExecutionPhase = ExecutionPhase + 1");
            // Notifications
            migrationBuilder.Sql("UPDATE Notifications SET UserType = UserType + 1");
            migrationBuilder.Sql("UPDATE Notifications SET NotificationType = NotificationType + 1");
            migrationBuilder.Sql("UPDATE Notifications SET NotificationChannel = NotificationChannel + 1");
            migrationBuilder.Sql("UPDATE Notifications SET Priority = Priority + 1");
            // Documents
            migrationBuilder.Sql("UPDATE Documents SET OwnerType = OwnerType + 1");
            migrationBuilder.Sql("UPDATE Documents SET DocumentType = DocumentType + 1");
            migrationBuilder.Sql("UPDATE Documents SET VerificationStatus = VerificationStatus + 1");
            // CustomsItems
            migrationBuilder.Sql("UPDATE CustomsItems SET ItemType = ItemType + 1");
            // CustomsDeclarations
            migrationBuilder.Sql("UPDATE CustomsDeclarations SET CustomsType = CustomsType + 1");
            // Locations
            migrationBuilder.Sql("UPDATE Locations SET LocationType = LocationType + 1");
            // Reports
            migrationBuilder.Sql("UPDATE Reports SET ReportType = ReportType + 1");
            // WeatherSnapshots
            migrationBuilder.Sql("UPDATE WeatherSnapshots SET FlightCategory = FlightCategory + 1");
            // Baggages
            migrationBuilder.Sql("UPDATE Baggages SET OwnerType = OwnerType + 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down: Rollback values from 1-based to 0-based
            migrationBuilder.Sql("UPDATE Orders SET OrderStatus = OrderStatus - 1");
            migrationBuilder.Sql("UPDATE Flights SET FlightStatus = FlightStatus - 1");
            migrationBuilder.Sql("UPDATE BoardingPasses SET BoardingStatus = BoardingStatus - 1");
            migrationBuilder.Sql("UPDATE Customers SET AccountStatus = AccountStatus - 1");
            migrationBuilder.Sql("UPDATE LoginLogs SET LoginStatus = LoginStatus - 1");
            migrationBuilder.Sql("UPDATE LoginLogs SET UserType = UserType - 1");
            migrationBuilder.Sql("UPDATE Payments SET PaymentStatus = PaymentStatus - 1");
            migrationBuilder.Sql("UPDATE Invoices SET InvoiceStatus = InvoiceStatus - 1");
            migrationBuilder.Sql("UPDATE Refunds SET RefundStatus = RefundStatus - 1");
            migrationBuilder.Sql("UPDATE OrderServices SET ServiceStatus = ServiceStatus - 1");
            migrationBuilder.Sql("UPDATE Services SET ServiceType = ServiceType - 1");
            migrationBuilder.Sql("UPDATE Employees SET JobRole = JobRole - 1");
            migrationBuilder.Sql("UPDATE Employees SET ShiftType = ShiftType - 1");
            migrationBuilder.Sql("UPDATE RefreshTokens SET UserType = UserType - 1");
            migrationBuilder.Sql("UPDATE PaymentMethods SET PaymentFunding = PaymentFunding - 1");
            migrationBuilder.Sql("UPDATE PassportValidations SET ValidationStatus = ValidationStatus - 1");
            migrationBuilder.Sql("UPDATE PackageServices SET ExecutionPhase = ExecutionPhase - 1");
            migrationBuilder.Sql("UPDATE Notifications SET UserType = UserType - 1");
            migrationBuilder.Sql("UPDATE Notifications SET NotificationType = NotificationType - 1");
            migrationBuilder.Sql("UPDATE Notifications SET NotificationChannel = NotificationChannel - 1");
            migrationBuilder.Sql("UPDATE Notifications SET Priority = Priority - 1");
            migrationBuilder.Sql("UPDATE Documents SET OwnerType = OwnerType - 1");
            migrationBuilder.Sql("UPDATE Documents SET DocumentType = DocumentType - 1");
            migrationBuilder.Sql("UPDATE Documents SET VerificationStatus = VerificationStatus - 1");
            migrationBuilder.Sql("UPDATE CustomsItems SET ItemType = ItemType - 1");
            migrationBuilder.Sql("UPDATE CustomsDeclarations SET CustomsType = CustomsType - 1");
            migrationBuilder.Sql("UPDATE Locations SET LocationType = LocationType - 1");
            migrationBuilder.Sql("UPDATE Reports SET ReportType = ReportType - 1");
            migrationBuilder.Sql("UPDATE WeatherSnapshots SET FlightCategory = FlightCategory - 1");
            migrationBuilder.Sql("UPDATE Baggages SET OwnerType = OwnerType - 1");
        }
    }
}
