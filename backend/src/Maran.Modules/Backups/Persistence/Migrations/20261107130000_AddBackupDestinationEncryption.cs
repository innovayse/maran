using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupDestinationEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveKey",
                schema: "backups",
                table: "BackupDestinations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EncryptArchives",
                schema: "backups",
                table: "BackupDestinations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveKey",
                schema: "backups",
                table: "BackupDestinations");

            migrationBuilder.DropColumn(
                name: "EncryptArchives",
                schema: "backups",
                table: "BackupDestinations");
        }
    }
}
