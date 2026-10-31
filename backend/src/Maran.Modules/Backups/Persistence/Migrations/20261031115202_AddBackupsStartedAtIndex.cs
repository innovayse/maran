using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <summary>
    /// Indexes a backup's start instant on its own, so the administrator's listing — newest first,
    /// with no account predicate — and its start-time window are served by an index rather than a
    /// sort of the whole journal (issue #196, docs/performance/list-indexes.md). Additive: the
    /// previous release reads nothing this changes.
    /// </summary>
    public partial class AddBackupsStartedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Backups_StartedAt",
                schema: "backups",
                table: "Backups",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Backups_StartedAt",
                schema: "backups",
                table: "Backups");
        }
    }
}
