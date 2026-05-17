using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAirportFromCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Checkpoints_Airports_AirportId",
                table: "Checkpoints");

            migrationBuilder.DropIndex(
                name: "IX_Checkpoints_AirportId",
                table: "Checkpoints");

            migrationBuilder.DropColumn(
                name: "AirportId",
                table: "Checkpoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AirportId",
                table: "Checkpoints",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Checkpoints_AirportId",
                table: "Checkpoints",
                column: "AirportId");

            migrationBuilder.AddForeignKey(
                name: "FK_Checkpoints_Airports_AirportId",
                table: "Checkpoints",
                column: "AirportId",
                principalTable: "Airports",
                principalColumn: "AirportId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
