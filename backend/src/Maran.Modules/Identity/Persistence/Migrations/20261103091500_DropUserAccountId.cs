using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropUserAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // contract-phase: Users.AccountId and UX_Users_AccountId, expanded when identity.AccountMemberships
            // was added (AddMembershipsPinsAndViewAs, backfilled from this column), unread by this release.
            // HONEST BOUNDARY, for the reviewer: the last tagged release (1.0.0-beta.4) DOES read this column,
            // because the memberships table did not exist then. Every reader in this release (sign-in,
            // refresh, switch, suspend and resume, e-mail change, invitation status, account deletion) reads
            // the memberships instead, and the one-owner-per-account guarantee this index gave is re-stated
            // below as UX_AccountMemberships_AccountId_Owner. Dropping it here therefore means 1.0.0-beta.4
            // cannot be rolled back onto this schema; the owner decides whether a pre-release is a rollback
            // target (issue #325 asks for exactly this removal).
            migrationBuilder.CreateIndex(
                name: "UX_AccountMemberships_AccountId_Owner",
                schema: "identity",
                table: "AccountMemberships",
                columns: new[] { "AccountId", "Role" },
                unique: true,
                filter: "\"Role\" = 'Owner'");

            migrationBuilder.DropIndex(
                name: "UX_Users_AccountId",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccountId",
                schema: "identity",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                schema: "identity",
                table: "Users",
                type: "uuid",
                nullable: true);

            // raw-sql: put the owned account back on the login it belongs to, from the owner membership.
            migrationBuilder.Sql(
                """
                UPDATE identity."Users" AS u
                SET "AccountId" = m."AccountId"
                FROM identity."AccountMemberships" AS m
                WHERE m."UserId" = u."Id" AND m."Role" = 'Owner';
                """);

            migrationBuilder.CreateIndex(
                name: "UX_Users_AccountId",
                schema: "identity",
                table: "Users",
                column: "AccountId",
                unique: true,
                filter: "\"AccountId\" IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "UX_AccountMemberships_AccountId_Owner",
                schema: "identity",
                table: "AccountMemberships");
        }
    }
}
