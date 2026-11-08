using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <summary>
    /// Adds the shell an account's system user was given (<c>Account.ShellAccess</c>) and the three
    /// ceilings an account holds in place of its plan's (<c>Account.LimitOverrides</c>: inodes,
    /// processes, open files), all chosen when the account is created (issue #194).
    /// </summary>
    /// <remarks>
    /// Expand only. <c>ShellAccess</c> is a required column with a database default, so every existing
    /// row reads <c>false</c>, which is the nologin shell each of those accounts has; the three
    /// overrides are nullable columns, and null is "follows the plan", which is what every existing
    /// account does. The previous release does not know any of the four columns and keeps inserting
    /// rows that the default and the nulls complete.
    /// </remarks>
    public partial class AddAccountLimitOverridesAndShell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShellAccess",
                schema: "accounts",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "InodeLimitOverride",
                schema: "accounts",
                table: "Accounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenFilesLimitOverride",
                schema: "accounts",
                table: "Accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessLimitOverride",
                schema: "accounts",
                table: "Accounts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessLimitOverride",
                schema: "accounts",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OpenFilesLimitOverride",
                schema: "accounts",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "InodeLimitOverride",
                schema: "accounts",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ShellAccess",
                schema: "accounts",
                table: "Accounts");
        }
    }
}
