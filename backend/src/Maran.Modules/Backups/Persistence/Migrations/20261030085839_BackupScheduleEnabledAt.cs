using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <summary>
    /// Gives a schedule's switch-on instant its own column, so <c>LastRunAt</c> holds only real runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until now switching a schedule on wrote the save instant into <c>LastRunAt</c>, so the screen
    /// showed a form save as "last run started" (issue #224). The column is added nullable and the
    /// data moves across in two set-based steps: every enabled row's stamp is copied into
    /// <c>EnabledAt</c>, and then <c>LastRunAt</c> is cleared wherever no scheduled backup started at
    /// or after it — that value was a save, never a run.
    /// </para>
    /// <para>
    /// A backup row records no schedule, so "started by the schedule" is read as a
    /// <c>Scheduled</c> backup of an account the schedule covers: the one account an override
    /// names, or any account for the host-wide row. A host-wide row whose only scheduled backups came
    /// from an override therefore keeps its stamp, which costs nothing: its anchor stays where it was.
    /// </para>
    /// </remarks>
    public partial class BackupScheduleEnabledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnabledAt",
                schema: "backups",
                table: "BackupSchedules",
                type: "timestamp with time zone",
                nullable: true);

            // raw-sql: a set-based backfill over every schedule row; no user input reaches it and it
            // takes no parameters, so there is nothing here for a parameterized query to carry.
            migrationBuilder.Sql(
                """
                UPDATE backups."BackupSchedules"
                SET "EnabledAt" = "LastRunAt"
                WHERE "Enabled" AND "LastRunAt" IS NOT NULL;
                """);

            // contract-phase: BackupSchedules.LastRunAt values that were switch-on saves, not runs —
            // the previous release reads the column as its anchor, so after a rollback a cleared
            // enabled schedule takes one backup at its next tick: an extra archive, never a lost one.
            // raw-sql: a set-based rewrite with no user input and no parameters to carry. 'Scheduled'
            // is BackupKind.Scheduled as BackupConfiguration persists it, by name.
            migrationBuilder.Sql(
                """
                UPDATE backups."BackupSchedules" AS schedule
                SET "LastRunAt" = NULL
                WHERE schedule."LastRunAt" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM backups."Backups" AS backup
                      WHERE backup."Kind" = 'Scheduled'
                        AND backup."StartedAt" >= schedule."LastRunAt"
                        AND (schedule."AccountId" IS NULL OR backup."AccountId" = schedule."AccountId"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // raw-sql: puts the switch-on instant back where the previous schema kept it — the later of
            // the two was that schema's anchor — so an enabled schedule does not become due at once
            // after the column is gone.
            migrationBuilder.Sql(
                """
                UPDATE backups."BackupSchedules"
                SET "LastRunAt" = "EnabledAt"
                WHERE "EnabledAt" IS NOT NULL
                  AND ("LastRunAt" IS NULL OR "LastRunAt" < "EnabledAt");
                """);

            migrationBuilder.DropColumn(
                name: "EnabledAt",
                schema: "backups",
                table: "BackupSchedules");
        }
    }
}
