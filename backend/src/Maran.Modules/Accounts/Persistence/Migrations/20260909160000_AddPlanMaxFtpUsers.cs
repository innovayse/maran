using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <summary>
    /// Adds each plan's FTPS-login allowance, and backfills the three standard plans.
    /// </summary>
    /// <remarks>
    /// This re-introduces a column name this schema has held before —
    /// <c>20260901230348_RenamePlanMaxFtpUsersToMaxSftpUsers</c> renamed the old one away — and it is
    /// NOT that rename reverted. The old column counted the panel's only file-transfer logins, which
    /// became the SFTP allowance when the SFTP module shipped and the panel installed no FTP daemon
    /// at all. This one counts logins of a SECOND daemon, served out of a different jail, which the
    /// panel now does install. Two allowances are correct here where one was correct then.
    ///
    /// The backfill is the same measure <c>AddPlanMaxCronEntries</c> takes and for the same reason:
    /// the column defaults to 0, the seeder only INSERTS plans that are absent and never updates one
    /// already there, and 0 is a legal allowance meaning "this plan includes no FTPS login". Without
    /// the backfill every customer on a shipped plan would be refused every FTPS login the day the
    /// feature was switched on, with a message naming their plan rather than the upgrade.
    ///
    /// The ids are the fixed ones in <c>PlanSeeder</c>, and the values match it. A plan an operator
    /// created themselves is left at 0 deliberately: we do not know what they intended, FTPS is off
    /// until they turn it on, and selling a customer an allowance the operator never chose is worse
    /// than making them choose one.
    /// </remarks>
    public partial class AddPlanMaxFtpUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxFtpUsers",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000001", 3);
            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000002", 10);
            UpdatePlan(migrationBuilder, "11111111-0000-4000-8000-000000000003", 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxFtpUsers",
                schema: "accounts",
                table: "Plans");
        }

        /// <summary>Sets one standard plan's FTPS-login allowance by its fixed id.</summary>
        /// <param name="migrationBuilder">The builder this migration writes through.</param>
        /// <param name="planId">The plan's fixed identity, as <c>PlanSeeder</c> declares it.</param>
        /// <param name="logins">The number of FTPS logins to allow.</param>
        private static void UpdatePlan(MigrationBuilder migrationBuilder, string planId, int logins)
        {
            migrationBuilder.UpdateData(
                schema: "accounts",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid(planId),
                column: "MaxFtpUsers",
                value: logins);
        }
    }
}
