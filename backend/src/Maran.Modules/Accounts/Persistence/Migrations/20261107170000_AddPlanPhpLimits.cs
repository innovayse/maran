using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPhpLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only (issue #138): four NOT NULL columns whose defaults are PhpLimits.Default, so every
            // plan that exists before this release reads as the conservative set, and the previous release,
            // which ignores the columns, keeps working against the new schema.
            migrationBuilder.AddColumn<int>(
                name: "PhpMemoryLimitMaxMb",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 256);

            migrationBuilder.AddColumn<int>(
                name: "PhpUploadMaxMb",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 64);

            migrationBuilder.AddColumn<int>(
                name: "PhpMaxExecutionSeconds",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "PhpMaxInputVars",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 3000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhpMemoryLimitMaxMb",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PhpUploadMaxMb",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PhpMaxExecutionSeconds",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PhpMaxInputVars",
                schema: "accounts",
                table: "Plans");
        }
    }
}
