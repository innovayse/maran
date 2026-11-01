using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Cron.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCronRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cron");

            migrationBuilder.CreateTable(
                name: "CronRuns",
                schema: "cron",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    Limit = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Stdout = table.Column<string>(type: "text", nullable: false),
                    Stderr = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CronRuns_AccountId_EntryId_StartedAt",
                schema: "cron",
                table: "CronRuns",
                columns: new[] { "AccountId", "EntryId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CronRuns",
                schema: "cron");
        }
    }
}
