using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasswordResetTokenUserForeignKey : Migration
    {
        // contract-phase: orphaned rows of identity."PasswordResetTokens" — no released version
        // reads them, on two independent grounds.
        //
        // First: there is no release. The panel has shipped no version, so there is no code a
        // customer could roll back to. That is the same ground RenamePlanMaxFtpUsersToMaxSftpUsers
        // stands on, and it stops being available the day 1.0 ships.
        //
        // Second, and this one survives 1.0: what is destroyed is not a column or a table, it is
        // the subset of rows whose "UserId" matches no row in identity."Users". Every release that
        // has ever existed reads those rows through exactly one path — ResetPasswordCommandHandler,
        // which looks the token up by hash and then loads its user, and refuses when that user is
        // null. An orphan therefore has exactly one observable behaviour in the previous release:
        // refusal. Deleting it produces the same refusal, from a token that is now simply not
        // found. So no release reads what this statement destroys in any sense that a rollback
        // could notice, and the schema after this migration is one the previous code still runs
        // against unchanged.
        //
        // The DELETE is also this migration's precondition rather than an opportunistic tidy-up:
        // the cascading foreign key below cannot be added over a row that already violates it.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The constraint cannot be added over a row that already violates it, and until now
            // nothing stopped a reset token outliving its user — which is the whole reason the
            // constraint is being added. Any such row is a permission to set a password on a login
            // that no longer exists, so removing it is both the migration's precondition and the
            // right thing to do with it. No caller-supplied value appears in this statement.
            // raw-sql: constant DDL-time cleanup, no parameters and no interpolation.
            migrationBuilder.Sql(
                """
                DELETE FROM identity."PasswordResetTokens" t
                WHERE NOT EXISTS (SELECT 1 FROM identity."Users" u WHERE u."Id" = t."UserId");
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_PasswordResetTokens_Users_UserId",
                schema: "identity",
                table: "PasswordResetTokens",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PasswordResetTokens_Users_UserId",
                schema: "identity",
                table: "PasswordResetTokens");
        }
    }
}
