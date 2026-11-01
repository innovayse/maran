using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ssl.Persistence.Migrations
{
    /// <summary>
    /// Records the panel's own trusted certificate, one row per host name (issue #132). Additive: a
    /// new table the previous release never reads.
    /// </summary>
    public partial class AddPanelCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PanelCertificates",
                schema: "ssl",
                columns: table => new
                {
                    HostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelCertificates", x => x.HostName);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PanelCertificates",
                schema: "ssl");
        }
    }
}
