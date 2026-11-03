using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResellerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                schema: "identity",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ResellerId",
                schema: "identity",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForceTwoFactorForResellers",
                schema: "identity",
                table: "SecurityPolicy",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_ResellerId",
                schema: "identity",
                table: "Users",
                column: "ResellerId",
                unique: true,
                filter: "\"ResellerId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Users_ResellerId",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ResellerId",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ForceTwoFactorForResellers",
                schema: "identity",
                table: "SecurityPolicy");
        }
    }
}
