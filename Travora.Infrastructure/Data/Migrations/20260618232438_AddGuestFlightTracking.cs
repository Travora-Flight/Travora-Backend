using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestFlightTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedFlights_CustomerId_FlightId",
                table: "SavedFlights");

            migrationBuilder.AlterColumn<int>(
                name: "CustomerId",
                table: "SavedFlights",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "GuestId",
                table: "SavedFlights",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedFlights_CustomerId_FlightId",
                table: "SavedFlights",
                columns: new[] { "CustomerId", "FlightId" },
                unique: true,
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SavedFlights_GuestId_FlightId",
                table: "SavedFlights",
                columns: new[] { "GuestId", "FlightId" },
                unique: true,
                filter: "[GuestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedFlights_CustomerId_FlightId",
                table: "SavedFlights");

            migrationBuilder.DropIndex(
                name: "IX_SavedFlights_GuestId_FlightId",
                table: "SavedFlights");

            migrationBuilder.DropColumn(
                name: "GuestId",
                table: "SavedFlights");

            migrationBuilder.AlterColumn<int>(
                name: "CustomerId",
                table: "SavedFlights",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedFlights_CustomerId_FlightId",
                table: "SavedFlights",
                columns: new[] { "CustomerId", "FlightId" },
                unique: true);
        }
    }
}
