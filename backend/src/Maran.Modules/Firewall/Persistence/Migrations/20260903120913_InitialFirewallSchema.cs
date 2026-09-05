using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Firewall.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFirewallSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "firewall");

            migrationBuilder.CreateTable(
                name: "BanEpisodes",
                schema: "firewall",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Failures = table.Column<int>(type: "integer", nullable: false),
                    BannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LiftedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BanEpisodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhitelistEntries",
                schema: "firewall",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cidr = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhitelistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhitelistSeedRecords",
                schema: "firewall",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cidr = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: false),
                    SeededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhitelistSeedRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BanEpisodes_ExpiresAt",
                schema: "firewall",
                table: "BanEpisodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_BanEpisodes_IpAddress_BannedAt",
                schema: "firewall",
                table: "BanEpisodes",
                columns: new[] { "IpAddress", "BannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BanEpisodes_IpAddress_WindowStart",
                schema: "firewall",
                table: "BanEpisodes",
                columns: new[] { "IpAddress", "WindowStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhitelistEntries_Cidr",
                schema: "firewall",
                table: "WhitelistEntries",
                column: "Cidr",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BanEpisodes",
                schema: "firewall");

            migrationBuilder.DropTable(
                name: "WhitelistEntries",
                schema: "firewall");

            migrationBuilder.DropTable(
                name: "WhitelistSeedRecords",
                schema: "firewall");
        }
    }
}
