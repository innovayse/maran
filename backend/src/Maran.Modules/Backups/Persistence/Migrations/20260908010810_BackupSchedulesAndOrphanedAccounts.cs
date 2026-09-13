using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackupSchedulesAndOrphanedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrphanedAccountUsername",
                schema: "backups",
                table: "Backups",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BackupSchedules",
                schema: "backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HourUtc = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeekUtc = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RetainCount = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_BackupSchedules_AccountId",
                schema: "backups",
                table: "BackupSchedules",
                column: "AccountId",
                unique: true,
                filter: "\"AccountId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupSchedules",
                schema: "backups");

            migrationBuilder.DropColumn(
                name: "OrphanedAccountUsername",
                schema: "backups",
                table: "Backups");
        }
    }
}
