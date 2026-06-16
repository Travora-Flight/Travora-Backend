using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateWeatherSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudLayers");

            migrationBuilder.DropColumn(
                name: "MetarType",
                table: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "RawObservation",
                table: "WeatherSnapshots");

            migrationBuilder.RenameColumn(
                name: "FlightCategory",
                table: "WeatherSnapshots",
                newName: "Humidity");

            migrationBuilder.RenameColumn(
                name: "Elevation",
                table: "WeatherSnapshots",
                newName: "ConditionCode");

            migrationBuilder.RenameColumn(
                name: "Dewpoint",
                table: "WeatherSnapshots",
                newName: "MinTemp");

            migrationBuilder.RenameColumn(
                name: "CloudCover",
                table: "WeatherSnapshots",
                newName: "Sunset");

            migrationBuilder.AddColumn<int>(
                name: "ChanceOfRain",
                table: "WeatherSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConditionIcon",
                table: "WeatherSnapshots",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConditionText",
                table: "WeatherSnapshots",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "FeelsLike",
                table: "WeatherSnapshots",
                type: "decimal(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxTemp",
                table: "WeatherSnapshots",
                type: "decimal(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Sunrise",
                table: "WeatherSnapshots",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChanceOfRain",
                table: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "ConditionIcon",
                table: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "ConditionText",
                table: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "FeelsLike",
                table: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "MaxTemp",
                table: "WeatherSnapshots");

            migrationBuilder.DropColumn(
                name: "Sunrise",
                table: "WeatherSnapshots");

            migrationBuilder.RenameColumn(
                name: "Sunset",
                table: "WeatherSnapshots",
                newName: "CloudCover");

            migrationBuilder.RenameColumn(
                name: "MinTemp",
                table: "WeatherSnapshots",
                newName: "Dewpoint");

            migrationBuilder.RenameColumn(
                name: "Humidity",
                table: "WeatherSnapshots",
                newName: "FlightCategory");

            migrationBuilder.RenameColumn(
                name: "ConditionCode",
                table: "WeatherSnapshots",
                newName: "Elevation");

            migrationBuilder.AddColumn<string>(
                name: "MetarType",
                table: "WeatherSnapshots",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawObservation",
                table: "WeatherSnapshots",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CloudLayers",
                columns: table => new
                {
                    CloudLayerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeatherSnapshotId = table.Column<int>(type: "int", nullable: false),
                    BaseAltitudeFeet = table.Column<int>(type: "int", nullable: false),
                    CoverType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudLayers", x => x.CloudLayerId);
                    table.ForeignKey(
                        name: "FK_CloudLayers_WeatherSnapshots_WeatherSnapshotId",
                        column: x => x.WeatherSnapshotId,
                        principalTable: "WeatherSnapshots",
                        principalColumn: "WeatherSnapshotId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudLayers_WeatherSnapshotId",
                table: "CloudLayers",
                column: "WeatherSnapshotId");
        }
    }
}
