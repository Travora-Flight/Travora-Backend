using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPickupDeliveryLocationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomsDeclarations_CustomsItems_CustomsTypeCustomsItemId",
                table: "CustomsDeclarations");

            migrationBuilder.DropIndex(
                name: "IX_CustomsDeclarations_CustomsTypeCustomsItemId",
                table: "CustomsDeclarations");

            migrationBuilder.RenameColumn(
                name: "CustomsTypeCustomsItemId",
                table: "CustomsDeclarations",
                newName: "CustomsType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CustomsType",
                table: "CustomsDeclarations",
                newName: "CustomsTypeCustomsItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomsDeclarations_CustomsTypeCustomsItemId",
                table: "CustomsDeclarations",
                column: "CustomsTypeCustomsItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomsDeclarations_CustomsItems_CustomsTypeCustomsItemId",
                table: "CustomsDeclarations",
                column: "CustomsTypeCustomsItemId",
                principalTable: "CustomsItems",
                principalColumn: "CustomsItemId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
