using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <summary>
    /// Adds each plan's per-pool php-fpm worker budget, and backfills the three standard plans.
    /// </summary>
    /// <remarks>
    /// The backfill is the whole point, not tidiness. The column defaults to 0, and the seeder only
    /// INSERTS plans that are absent — it never updates one that is already there — so on every
    /// existing installation the three standard plans would keep 0. That value is passed straight
    /// through as <c>pm.max_children</c>, which php-fpm refuses to start a pool with: the first PHP
    /// version change after an upgrade would take the customer's site down. An upgrade must not be
    /// able to break a server that was working.
    ///
    /// The ids are the fixed ones in <c>PlanSeeder</c>, and the values match it. A plan an operator
    /// created themselves is left at 0 deliberately — we do not know what they intended, and the
    /// domain refuses a non-positive budget at the boundary rather than guessing here.
    /// </remarks>
    public partial class AddPlanMaxPhpWorkersPerPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxPhpWorkersPerPool",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000001", 5);
            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000002", 10);
            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000003", 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxPhpWorkersPerPool",
                schema: "accounts",
                table: "Plans");
        }

        /// <summary>Sets one standard plan's per-pool worker budget by its fixed id.</summary>
        /// <param name="migrationBuilder">The builder this migration writes through.</param>
        /// <param name="planId">The plan's fixed identity, as <c>PlanSeeder</c> declares it.</param>
        /// <param name="workers">The per-pool budget to set.</param>
        private static void UpdatePlan(MigrationBuilder migrationBuilder, string planId, int workers)
        {
            migrationBuilder.UpdateData(
                schema: "accounts",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid(planId),
                column: "MaxPhpWorkersPerPool",
                value: workers);
        }
    }
}
