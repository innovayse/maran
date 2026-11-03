using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "State",
                schema: "identity",
                table: "AccountMemberships",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            // raw-sql: one-time expand backfill, parameter-free. Until now a suspension was stored on the
            // owner's login (Users."Lifecycle" Suspended or InvitedAccountSuspended); it is now the state
            // of the membership, so every membership of an account whose owner login was suspended
            // (the owner's own and every added member's) becomes Suspended (issue #326). Idempotent.
            migrationBuilder.Sql(
                """
                UPDATE identity."AccountMemberships" AS m
                SET "State" = 'Suspended'
                FROM identity."Users" AS u
                WHERE u."AccountId" = m."AccountId"
                  AND u."Lifecycle" IN ('Suspended', 'InvitedAccountSuspended');
                """);

            // raw-sql: the login itself is no longer suspendable, so the two suspended lifecycles collapse
            // into the claimed and the unclaimed one, and the legacy State / IsAccountSuspended pair is
            // restated to agree with what the application now writes. Lifecycle was added after the last
            // release (AddUserLifecycle), so no release reads the two values this removes; the legacy
            // pair is rewritten for agreement only. Idempotent.
            migrationBuilder.Sql(
                """
                UPDATE identity."Users" SET
                    "Lifecycle" = CASE "Lifecycle" WHEN 'Suspended' THEN 'Active' ELSE 'Invited' END,
                    "State" = CASE "Lifecycle" WHEN 'Suspended' THEN 'Active' ELSE 'Invited' END,
                    "IsAccountSuspended" = false
                WHERE "Lifecycle" IN ('Suspended', 'InvitedAccountSuspended');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // raw-sql: restore the login-wide suspension the previous shape stored, from the owner's
            // membership, before the column that holds the truth is dropped.
            migrationBuilder.Sql(
                """
                UPDATE identity."Users" AS u SET
                    "Lifecycle" = CASE u."Lifecycle" WHEN 'Active' THEN 'Suspended' ELSE 'InvitedAccountSuspended' END,
                    "State" = CASE u."Lifecycle" WHEN 'Active' THEN 'Suspended' ELSE 'Invited' END,
                    "IsAccountSuspended" = true
                FROM identity."AccountMemberships" AS m
                WHERE m."UserId" = u."Id" AND m."Role" = 'Owner' AND m."State" = 'Suspended';
                """);

            migrationBuilder.DropColumn(
                name: "State",
                schema: "identity",
                table: "AccountMemberships");
        }
    }
}
