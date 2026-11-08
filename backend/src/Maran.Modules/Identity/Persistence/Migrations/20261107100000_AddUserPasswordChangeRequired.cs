using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordChangeRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PasswordChangeRequired",
                schema: "identity",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordChangeRequired",
                schema: "identity",
                table: "Users");
        }
    }
}
