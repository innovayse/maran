using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Dns.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDnsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only: a new schema with new tables, so the previous release, which knows none of it,
            // keeps working against the new database.
            migrationBuilder.EnsureSchema(
                name: "dns");

            migrationBuilder.CreateTable(
                name: "DnsSettings",
                schema: "dns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryNameserver = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    SecondaryNameserver = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    HostmasterMailbox = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    PublicIpv4 = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    PublicIpv6 = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    DefaultTtl = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                schema: "dns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Serial = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZoneRecords",
                schema: "dns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Ttl = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    Tag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneRecords_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalSchema: "dns",
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Zones_AccountId",
                schema: "dns",
                table: "Zones",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Zones_Name",
                schema: "dns",
                table: "Zones",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRecords_NaturalKey",
                schema: "dns",
                table: "ZoneRecords",
                columns: new[] { "ZoneId", "Name", "Type", "Value", "Priority", "Weight", "Port", "Flags", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRecords_ZoneId_Name",
                schema: "dns",
                table: "ZoneRecords",
                columns: new[] { "ZoneId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DnsSettings",
                schema: "dns");

            migrationBuilder.DropTable(
                name: "ZoneRecords",
                schema: "dns");

            migrationBuilder.DropTable(
                name: "Zones",
                schema: "dns");
        }
    }
}
