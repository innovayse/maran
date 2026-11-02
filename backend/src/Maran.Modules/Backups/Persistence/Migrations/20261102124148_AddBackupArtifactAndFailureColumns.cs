using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupArtifactAndFailureColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtifactSha256",
                schema: "backups",
                table: "Backups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedAccountUsername",
                schema: "backups",
                table: "Backups",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunFailureCode",
                schema: "backups",
                table: "Backups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // raw-sql: copy the previous release's artifact digest, failure code and orphaned-account name into the
            // nullable columns the code now reads. Empty strings were the old spelling of "none". Idempotent and
            // parameter-free; the old columns stay written by the entity.
            migrationBuilder.Sql("""
                UPDATE backups."Backups" SET "ArtifactSha256" = NULLIF("Sha256", '')
                    WHERE "Status" = 'Completed' AND "ArtifactSha256" IS NULL;
                UPDATE backups."Backups" SET "RunFailureCode" = NULLIF("FailureCode", '')
                    WHERE "Status" = 'Failed' AND "RunFailureCode" IS NULL;
                UPDATE backups."Backups" SET "DeletedAccountUsername" = NULLIF("OrphanedAccountUsername", '')
                    WHERE "DeletedAccountUsername" IS NULL;
                """);

            // raw-sql: make existing schedule rows satisfy the check constraints created below: a weekly row without
            // a weekday was read as daily, a daily row keeps no weekday, and the legacy Enabled flag is made to
            // agree with EnabledAt (a flag set without the instant never had a next run, so it reads as off).
            // Parameter-free; runs BEFORE the constraints.
            migrationBuilder.Sql("""
                UPDATE backups."BackupSchedules" SET "Frequency" = 'Daily'
                    WHERE "Frequency" = 'Weekly' AND "DayOfWeekUtc" IS NULL;
                UPDATE backups."BackupSchedules" SET "DayOfWeekUtc" = NULL
                    WHERE "Frequency" = 'Daily' AND "DayOfWeekUtc" IS NOT NULL;
                UPDATE backups."BackupSchedules" SET "Enabled" = FALSE WHERE "Enabled" AND "EnabledAt" IS NULL;
                UPDATE backups."BackupSchedules" SET "EnabledAt" = NULL WHERE NOT "Enabled" AND "EnabledAt" IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BackupSchedules_EnabledMatchesEnabledAt",
                schema: "backups",
                table: "BackupSchedules",
                sql: "\"Enabled\" = (\"EnabledAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BackupSchedules_WeekdayOnlyWhenWeekly",
                schema: "backups",
                table: "BackupSchedules",
                sql: "(\"Frequency\" = 'Weekly') = (\"DayOfWeekUtc\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BackupSchedules_EnabledMatchesEnabledAt",
                schema: "backups",
                table: "BackupSchedules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BackupSchedules_WeekdayOnlyWhenWeekly",
                schema: "backups",
                table: "BackupSchedules");

            migrationBuilder.DropColumn(
                name: "ArtifactSha256",
                schema: "backups",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "DeletedAccountUsername",
                schema: "backups",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "RunFailureCode",
                schema: "backups",
                table: "Backups");
        }
    }
}
