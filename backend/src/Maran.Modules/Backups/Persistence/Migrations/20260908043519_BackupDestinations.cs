using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <summary>
    /// Adds the destinations table, the schedule's pointer at it, and the two foreign keys the
    /// existing pointers were waiting for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purely additive, and <c>Backup.DestinationId</c> is NOT narrowed.</b> Narrowing it would be
    /// an <c>AlterColumn</c> against a release that writes null on every insert, which is what the
    /// expand-then-contract law refuses and what no honest <c>// contract-phase:</c> line could
    /// excuse. The nulls already there keep the meaning they were written with — "the configured
    /// local root", which is now the default destination row — and every new row carries a real id.
    /// </para>
    /// <para>
    /// <b>No row is seeded here.</b> The default destination is written at startup by
    /// <c>DefaultBackupDestinationSeeder</c>. Its <c>Path</c> column was filled from the panel
    /// setting <c>Backups__LocalRoot</c> when this migration shipped; that setting has since been
    /// removed, because nothing made it equal to the directory the agent actually writes into, and
    /// the column now stays empty for a local destination — the path a screen shows is read from the
    /// agent. The column itself remains until a contract-phase migration removes it.
    /// </para>
    /// <para>
    /// The two foreign keys are <c>Restrict</c>. Every existing row satisfies them by carrying null,
    /// so the previous release keeps running against this schema.
    /// </para>
    /// </remarks>
    public partial class BackupDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationId",
                schema: "backups",
                table: "BackupSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupDestinations",
                schema: "backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false, defaultValue: ""),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupDestinations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupSchedules_DestinationId",
                schema: "backups",
                table: "BackupSchedules",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_DestinationId",
                schema: "backups",
                table: "Backups",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupDestinations_IsDefault",
                schema: "backups",
                table: "BackupDestinations",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_BackupDestinations_Name",
                schema: "backups",
                table: "BackupDestinations",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Backups_BackupDestinations_DestinationId",
                schema: "backups",
                table: "Backups",
                column: "DestinationId",
                principalSchema: "backups",
                principalTable: "BackupDestinations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupSchedules_BackupDestinations_DestinationId",
                schema: "backups",
                table: "BackupSchedules",
                column: "DestinationId",
                principalSchema: "backups",
                principalTable: "BackupDestinations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Backups_BackupDestinations_DestinationId",
                schema: "backups",
                table: "Backups");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupSchedules_BackupDestinations_DestinationId",
                schema: "backups",
                table: "BackupSchedules");

            migrationBuilder.DropTable(
                name: "BackupDestinations",
                schema: "backups");

            migrationBuilder.DropIndex(
                name: "IX_BackupSchedules_DestinationId",
                schema: "backups",
                table: "BackupSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Backups_DestinationId",
                schema: "backups",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                schema: "backups",
                table: "BackupSchedules");
        }
    }
}
