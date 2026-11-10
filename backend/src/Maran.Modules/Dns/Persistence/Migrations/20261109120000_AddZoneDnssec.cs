using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Dns.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneDnssec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only: two columns on Zones (one not null with a default of zero, meaning off, and one
            // nullable) and one new table. The previous release reads none of them and keeps working against
            // the new schema; every zone that exists keeps being served unsigned.
            migrationBuilder.AddColumn<int>(
                name: "DnssecState",
                schema: "dns",
                table: "Zones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DnssecWithdrawalStartedAt",
                schema: "dns",
                table: "Zones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ZoneSigningKeys",
                schema: "dns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PrivateKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    Algorithm = table.Column<int>(type: "integer", nullable: false),
                    KeyTag = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneSigningKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneSigningKeys_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalSchema: "dns",
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneSigningKeys_ZoneId",
                schema: "dns",
                table: "ZoneSigningKeys",
                column: "ZoneId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZoneSigningKeys",
                schema: "dns");

            migrationBuilder.DropColumn(
                name: "DnssecWithdrawalStartedAt",
                schema: "dns",
                table: "Zones");

            migrationBuilder.DropColumn(
                name: "DnssecState",
                schema: "dns",
                table: "Zones");
        }
    }
}
