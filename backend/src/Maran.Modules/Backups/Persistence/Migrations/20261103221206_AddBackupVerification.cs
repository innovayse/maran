using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Backups.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationFailureCode",
                schema: "backups",
                table: "Backups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationState",
                schema: "backups",
                table: "Backups",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NotVerified");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                schema: "backups",
                table: "Backups",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationFailureCode",
                schema: "backups",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "VerificationState",
                schema: "backups",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                schema: "backups",
                table: "Backups");
        }
    }
}
