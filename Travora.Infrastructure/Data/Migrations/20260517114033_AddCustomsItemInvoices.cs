using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomsItemInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseInvoicePath",
                table: "CustomsItems");

            migrationBuilder.CreateTable(
                name: "CustomsItemInvoices",
                columns: table => new
                {
                    CustomsItemInvoiceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomsItemId = table.Column<int>(type: "int", nullable: false),
                    InvoicePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsItemInvoices", x => x.CustomsItemInvoiceId);
                    table.ForeignKey(
                        name: "FK_CustomsItemInvoices_CustomsItems_CustomsItemId",
                        column: x => x.CustomsItemId,
                        principalTable: "CustomsItems",
                        principalColumn: "CustomsItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsItemInvoices_CustomsItemId",
                table: "CustomsItemInvoices",
                column: "CustomsItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomsItemInvoices");

            migrationBuilder.AddColumn<string>(
                name: "PurchaseInvoicePath",
                table: "CustomsItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }
    }
}
