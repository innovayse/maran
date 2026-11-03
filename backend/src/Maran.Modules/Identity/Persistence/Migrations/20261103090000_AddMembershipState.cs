using System;
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

            // The one-owner-per-account guarantee, stated on the memberships so it no longer depends on
            // Users."AccountId" (kept, with its own unique index UX_Users_AccountId, for the previous
            // release and dropped in the release after this one). Keyed on (AccountId, Role) rather than
            // on AccountId alone because EF Core refuses a second index over the same property set.
            migrationBuilder.CreateIndex(
                name: "UX_AccountMemberships_AccountId_Owner",
                schema: "identity",
                table: "AccountMemberships",
                columns: new[] { "AccountId", "Role" },
                unique: true,
                filter: "\"Role\" = 'Owner'");

            // raw-sql: one-time expand backfill, parameter-free. Until now a suspension was stored on the
            // owner's login (Users."Lifecycle" Suspended or InvitedAccountSuspended); it is now the state
            // of the membership, so every membership of an account whose owner login was suspended
            // (the owner's own and every added member's) becomes Suspended (issue #326). It reads
            // Users."AccountId", which this release keeps but no longer writes, so it matters only for
            // logins created by an earlier release.
            migrationBuilder.Sql(
                """
                UPDATE identity."AccountMemberships" AS m
                SET "State" = 'Suspended'
                FROM identity."Users" AS u
                WHERE u."AccountId" = m."AccountId"
                  AND u."Lifecycle" IN ('Suspended', 'InvitedAccountSuspended');
                """);

            // raw-sql: the login itself is no longer suspendable, so the two suspended lifecycles collapse
            // into the claimed and the unclaimed one. The legacy State / IsAccountSuspended pair is left
            // exactly as it is: the previous release reads it to refuse the owner of a suspended account,
            // so blanking it would let that owner sign in again after a rollback. Lifecycle was added
            // after the last release (AddUserLifecycle), so no release reads the two values this removes.
            migrationBuilder.Sql(
                """
                UPDATE identity."Users" SET
                    "Lifecycle" = CASE "Lifecycle" WHEN 'Suspended' THEN 'Active' ELSE 'Invited' END
                WHERE "Lifecycle" IN ('Suspended', 'InvitedAccountSuspended');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // raw-sql: restore the login-wide suspension the previous shape stored, from the OWNER's
            // membership only, before the column that holds the truth is dropped. The legacy pair is
            // restored to the same values the application writes for a suspended owner (State
            // Suspended for a claimed login, Invited for an unclaimed one, IsAccountSuspended true), so
            // it agrees with Lifecycle whether or not the pair was kept in step. An added member's
            // suspension is not restored: the previous shape had no per-member suspension to restore it to.
            migrationBuilder.Sql(
                """
                UPDATE identity."Users" AS u SET
                    "Lifecycle" = CASE u."Lifecycle" WHEN 'Active' THEN 'Suspended' ELSE 'InvitedAccountSuspended' END,
                    "State" = CASE u."Lifecycle" WHEN 'Active' THEN 'Suspended' ELSE 'Invited' END,
                    "IsAccountSuspended" = true
                FROM identity."AccountMemberships" AS m
                WHERE m."UserId" = u."Id" AND m."Role" = 'Owner' AND m."State" = 'Suspended';
                """);

            migrationBuilder.DropIndex(
                name: "UX_AccountMemberships_AccountId_Owner",
                schema: "identity",
                table: "AccountMemberships");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "identity",
                table: "AccountMemberships");
        }
    }
}
