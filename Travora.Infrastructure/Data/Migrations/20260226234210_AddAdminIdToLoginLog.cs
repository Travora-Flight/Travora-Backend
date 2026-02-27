using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminIdToLoginLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminId",
                table: "LoginLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginLogs_AdminId",
                table: "LoginLogs",
                column: "AdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_LoginLogs_Admins_AdminId",
                table: "LoginLogs",
                column: "AdminId",
                principalTable: "Admins",
                principalColumn: "AdminId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoginLogs_Admins_AdminId",
                table: "LoginLogs");

            migrationBuilder.DropIndex(
                name: "IX_LoginLogs_AdminId",
                table: "LoginLogs");

            migrationBuilder.DropColumn(
                name: "AdminId",
                table: "LoginLogs");
        }
    }
}
