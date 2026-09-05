using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlanMaxFtpUsersToMaxSftpUsers : Migration
    {
        // contract-phase: Plans.MaxFtpUsers, never shipped — no released version reads it.
        //
        // A rename is a removal to whatever ran before it, so `maran migrate guard` refuses one by
        // default. It is safe HERE and only here: the panel has had no release, so there is no
        // version of the code a customer could roll back to that would look for MaxFtpUsers. The
        // day 1.0 ships this exemption stops being available, and the same change would have to be
        // made as an expand — add MaxSftpUsers, backfill it, and drop MaxFtpUsers a release later.

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
