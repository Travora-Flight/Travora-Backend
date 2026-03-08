using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerUsernameAndPassportMrzFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckComposite",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckDateOfBirth",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckExpirationDate",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckNumber",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckPersonalNumber",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractedDateOfBirth",
                table: "PassportValidations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractedExpiryDate",
                table: "PassportValidations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedGender",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedGivenNames",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedNationality",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedPassportNumber",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedPersonalNumber",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedSurname",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MrzMethod",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MrzType",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawMrzText",
                table: "PassportValidations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidComposite",
                table: "PassportValidations",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidDateOfBirth",
                table: "PassportValidations",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidExpirationDate",
                table: "PassportValidations",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidNumber",
                table: "PassportValidations",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidPersonalNumber",
                table: "PassportValidations",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidScore",
                table: "PassportValidations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Customers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckComposite",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "CheckDateOfBirth",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "CheckExpirationDate",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "CheckNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "CheckPersonalNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedDateOfBirth",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedExpiryDate",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedGender",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedGivenNames",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedNationality",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedPassportNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedPersonalNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ExtractedSurname",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "MrzMethod",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "MrzType",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "RawMrzText",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidComposite",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidDateOfBirth",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidExpirationDate",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidPersonalNumber",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "ValidScore",
                table: "PassportValidations");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Customers");
        }
    }
}
