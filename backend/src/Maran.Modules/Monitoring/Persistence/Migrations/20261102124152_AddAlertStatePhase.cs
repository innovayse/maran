using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Monitoring.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertStatePhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Phase",
                schema: "monitoring",
                table: "AlertStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // raw-sql: derive Phase from the previous release's IsFiring / ConsecutiveBreaches (Healthy 0 is the column
            // default), then make RaisedAt agree with it so the check constraint below holds: it exists exactly
            // while an episode is open (a firing row without it takes LastObservedAt, any other row clears it).
            // Parameter-free; runs BEFORE the constraint.
            migrationBuilder.Sql("""
                UPDATE monitoring."AlertStates" SET "Phase" = 2 WHERE "IsFiring";
                UPDATE monitoring."AlertStates" SET "Phase" = 1 WHERE NOT "IsFiring" AND "ConsecutiveBreaches" > 0;
                UPDATE monitoring."AlertStates" SET "RaisedAt" = "LastObservedAt" WHERE "Phase" = 2 AND "RaisedAt" IS NULL;
                UPDATE monitoring."AlertStates" SET "RaisedAt" = NULL WHERE "Phase" IN (0, 1) AND "RaisedAt" IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AlertStates_Phase_RaisedAt",
                schema: "monitoring",
                table: "AlertStates",
                sql: "(\"Phase\" = 2 AND \"RaisedAt\" IS NOT NULL) OR (\"Phase\" IN (0, 1) AND \"RaisedAt\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AlertStates_Phase_RaisedAt",
                schema: "monitoring",
                table: "AlertStates");

            migrationBuilder.DropColumn(
                name: "Phase",
                schema: "monitoring",
                table: "AlertStates");
        }
    }
}
