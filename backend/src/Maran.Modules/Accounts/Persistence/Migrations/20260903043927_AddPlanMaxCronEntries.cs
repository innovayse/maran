using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <summary>
    /// Adds each plan's scheduled-task allowance, and backfills the three standard plans.
    /// </summary>
    /// <remarks>
    /// The backfill is the whole point, not tidiness. The column defaults to 0, and the seeder only
    /// INSERTS plans that are absent — it never updates one that is already there — so on every
    /// existing installation the three standard plans would keep 0, and 0 is a legal allowance
    /// meaning "this plan includes no scheduled tasks". So the failure is silent and total: after an
    /// upgrade every customer on a shipped plan is refused every cron entry, with a message that
    /// names their plan rather than the upgrade. An upgrade must not be able to take a feature away
    /// from a server that was working.
    ///
    /// The ids are the fixed ones in <c>PlanSeeder</c>, and the values match it. A plan an operator
    /// created themselves is left at 0 deliberately — we do not know what they intended, and unlike
    /// the php-worker budget a zero here is a coherent product decision the domain accepts rather
    /// than a broken plan, so guessing on their behalf would silently sell something they did not.
    /// </remarks>
    public partial class AddPlanMaxCronEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxCronEntries",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000001", 5);
            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000002", 20);
            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000003", 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxCronEntries",
                schema: "accounts",
                table: "Plans");
        }

        /// <summary>Sets one standard plan's cron allowance by its fixed id.</summary>
        /// <param name="migrationBuilder">The builder this migration writes through.</param>
        /// <param name="planId">The plan's fixed identity, as <c>PlanSeeder</c> declares it.</param>
        /// <param name="entries">The number of cron entries to allow.</param>
        private static void UpdatePlan(MigrationBuilder migrationBuilder, string planId, int entries)
        {
            migrationBuilder.UpdateData(
                schema: "accounts",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid(planId),
                column: "MaxCronEntries",
                value: entries);
        }
    }
}
