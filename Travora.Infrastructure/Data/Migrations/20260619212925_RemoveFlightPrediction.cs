using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFlightPrediction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlightPredictions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlightPredictions",
                columns: table => new
                {
                    PredictionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlightId = table.Column<int>(type: "int", nullable: false),
                    WeatherSnapshotId = table.Column<int>(type: "int", nullable: false),
                    ActualDelayMinutes = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FactorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    PredictionAccuracy = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    PredictionConfidenceScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PredictionModelVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PredictionTimestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightPredictions", x => x.PredictionId);
                    table.ForeignKey(
                        name: "FK_FlightPredictions_Flights_FlightId",
                        column: x => x.FlightId,
                        principalTable: "Flights",
                        principalColumn: "FlightId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlightPredictions_WeatherSnapshots_WeatherSnapshotId",
                        column: x => x.WeatherSnapshotId,
                        principalTable: "WeatherSnapshots",
                        principalColumn: "WeatherSnapshotId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlightPredictions_FlightId",
                table: "FlightPredictions",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightPredictions_WeatherSnapshotId",
                table: "FlightPredictions",
                column: "WeatherSnapshotId");
        }
    }
}
