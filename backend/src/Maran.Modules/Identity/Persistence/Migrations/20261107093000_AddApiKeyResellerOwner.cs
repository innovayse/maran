using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyResellerOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResellerId",
                schema: "identity",
                table: "ApiKeys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_ResellerId",
                schema: "identity",
                table: "ApiKeys",
                column: "ResellerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_ResellerId",
                schema: "identity",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ResellerId",
                schema: "identity",
                table: "ApiKeys");
        }
    }
}
