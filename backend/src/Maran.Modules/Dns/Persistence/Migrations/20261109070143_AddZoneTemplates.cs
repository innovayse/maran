using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Dns.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only: one nullable column and two new tables. The previous release reads none of them
            // and keeps working against the new schema; the built-in rows are written by the start-up seeder.
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultTemplateId",
                schema: "dns",
                table: "DnsSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ZoneTemplates",
                schema: "dns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZoneTemplateRecords",
                schema: "dns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Ttl = table.Column<int>(type: "integer", nullable: true),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    Tag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneTemplateRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneTemplateRecords_ZoneTemplates_ZoneTemplateId",
                        column: x => x.ZoneTemplateId,
                        principalSchema: "dns",
                        principalTable: "ZoneTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneTemplateRecords_ZoneTemplateId_Position",
                schema: "dns",
                table: "ZoneTemplateRecords",
                columns: new[] { "ZoneTemplateId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ZoneTemplates_NameKey",
                schema: "dns",
                table: "ZoneTemplates",
                column: "NameKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZoneTemplateRecords",
                schema: "dns");

            migrationBuilder.DropTable(
                name: "ZoneTemplates",
                schema: "dns");

            migrationBuilder.DropColumn(
                name: "DefaultTemplateId",
                schema: "dns",
                table: "DnsSettings");
        }
    }
}
