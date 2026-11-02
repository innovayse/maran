using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Cron.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCronRunEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EndExitCode",
                schema: "cron",
                table: "CronRuns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndKind",
                schema: "cron",
                table: "CronRuns",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "EndLimit",
                schema: "cron",
                table: "CronRuns",
                type: "integer",
                nullable: true);

            // raw-sql: copy the stored end of rows written by the previous release into the new columns
            // (3 = CronRunEndKind.Stopped, 1 = Exited, 2 = WithoutStatus). Parameter-free; the old columns
            // were never touched.
            migrationBuilder.Sql("""
                UPDATE cron."CronRuns"
                SET "EndKind" = CASE
                        WHEN "Limit" IS NOT NULL THEN 3
                        WHEN "ExitCode" IS NOT NULL THEN 1
                        ELSE 2
                    END,
                    "EndExitCode" = CASE WHEN "Limit" IS NULL THEN "ExitCode" END,
                    "EndLimit" = "Limit";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndExitCode",
                schema: "cron",
                table: "CronRuns");

            migrationBuilder.DropColumn(
                name: "EndKind",
                schema: "cron",
                table: "CronRuns");

            migrationBuilder.DropColumn(
                name: "EndLimit",
                schema: "cron",
                table: "CronRuns");
        }
    }
}
