using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCodeShareFlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeShareFlights");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeShareFlights",
                columns: table => new
                {
                    CodeShareId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MarketingAirlineId = table.Column<int>(type: "int", nullable: false),
                    OperatingFlightId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MarketingAirlineName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MarketingFlightNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MarketingIataNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MarketingIcaoNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeShareFlights", x => x.CodeShareId);
                    table.ForeignKey(
                        name: "FK_CodeShareFlights_Airlines_MarketingAirlineId",
                        column: x => x.MarketingAirlineId,
                        principalTable: "Airlines",
                        principalColumn: "AirlineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CodeShareFlights_Flights_OperatingFlightId",
                        column: x => x.OperatingFlightId,
                        principalTable: "Flights",
                        principalColumn: "FlightId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeShareFlights_MarketingAirlineId",
                table: "CodeShareFlights",
                column: "MarketingAirlineId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeShareFlights_OperatingFlightId",
                table: "CodeShareFlights",
                column: "OperatingFlightId");
        }
    }
}
