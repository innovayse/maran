using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lifecycle",
                schema: "identity",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");

            // raw-sql: backfill the new column from the legacy State / IsAccountSuspended pair, which the
            // previous release still writes. Idempotent, so re-running it after a rollback restores agreement.
            migrationBuilder.Sql("""
                UPDATE identity."Users" SET "Lifecycle" = CASE
                    WHEN "State" = 'Suspended' THEN 'Suspended'
                    WHEN "State" = 'Invited' AND "IsAccountSuspended" THEN 'InvitedAccountSuspended'
                    WHEN "State" = 'Invited' THEN 'Invited'
                    ELSE 'Active' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lifecycle",
                schema: "identity",
                table: "Users");
        }
    }
}
