using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedLoginByIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FailedLoginByIp",
                schema: "identity",
                columns: table => new
                {
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Failures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedLoginByIp", x => x.IpAddress);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailedLoginByIp_WindowStart",
                schema: "identity",
                table: "FailedLoginByIp",
                column: "WindowStart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailedLoginByIp",
                schema: "identity");
        }
    }
}
