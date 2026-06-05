using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOcrPersonalNumberFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckPersonalNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedPersonalNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidPersonalNumber",
                table: "PassportValidations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckPersonalNumber",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedPersonalNumber",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidPersonalNumber",
                table: "PassportValidations",
                type: "bit",
                nullable: true);
        }
    }
}
