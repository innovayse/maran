using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ftp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFtpSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ftp");

            migrationBuilder.CreateTable(
                name: "FtpsSettings",
                schema: "ftp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PassivePortMin = table.Column<int>(type: "integer", nullable: false),
                    PassivePortMax = table.Column<int>(type: "integer", nullable: false),
                    PassiveAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    MaxClients = table.Column<int>(type: "integer", nullable: false),
                    Ipv4Only = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FtpsSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FtpsSettings",
                schema: "ftp");
        }
    }
}
