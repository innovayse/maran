using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ftp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFtpUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FtpUsers",
                schema: "ftp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FtpUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FtpUsers_AccountId",
                schema: "ftp",
                table: "FtpUsers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FtpUsers_AccountId_Name",
                schema: "ftp",
                table: "FtpUsers",
                columns: new[] { "AccountId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FtpUsers_FullName",
                schema: "ftp",
                table: "FtpUsers",
                column: "FullName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FtpUsers",
                schema: "ftp");
        }
    }
}
