using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlanMaxFtpUsersToMaxSftpUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxFtpUsers",
                schema: "accounts",
                table: "Plans",
                newName: "MaxSftpUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxSftpUsers",
                schema: "accounts",
                table: "Plans",
                newName: "MaxFtpUsers");
        }
    }
}
