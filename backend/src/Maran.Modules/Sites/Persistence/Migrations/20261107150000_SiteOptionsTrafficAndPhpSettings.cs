using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sites.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteOptionsTrafficAndPhpSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only: one added column with a default, and the added tables (traffic, per-site
            // options and their three child tables, and the customers' PHP settings). Nothing the previous
            // release reads is dropped, renamed or narrowed, so it keeps running against this schema.
            migrationBuilder.AddColumn<int>(
                name: "LogRetentionDays",
                schema: "sites",
                table: "Sites",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.CreateTable(
                name: "SiteTrafficCursors",
                schema: "sites",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IncompleteSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteTrafficCursors", x => x.SiteId);
                    table.ForeignKey(
                        name: "FK_SiteTrafficCursors_Sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteTrafficDays",
                schema: "sites",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Requests = table.Column<long>(type: "bigint", nullable: false),
                    BodyBytes = table.Column<long>(type: "bigint", nullable: false),
                    OtherPathRequests = table.Column<long>(type: "bigint", nullable: false),
                    Statuses = table.Column<string>(type: "jsonb", nullable: false),
                    Paths = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteTrafficDays", x => new { x.SiteId, x.Date });
                    table.ForeignKey(
                        name: "FK_SiteTrafficDays_Sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhpSettings",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhpSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteOptions",
                schema: "sites",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalHost = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ClientMaxBodySizeMib = table.Column<int>(type: "integer", nullable: true),
                    IpAccessMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    StaticCacheDays = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteOptions", x => x.SiteId);
                    table.ForeignKey(
                        name: "FK_SiteOptions_Sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteBasicAuthUsers",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteBasicAuthUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteBasicAuthUsers_SiteOptions_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "SiteOptions",
                        principalColumn: "SiteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteIpRules",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteIpRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteIpRules_SiteOptions_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "SiteOptions",
                        principalColumn: "SiteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteRedirects",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Match = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteRedirects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteRedirects_SiteOptions_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "SiteOptions",
                        principalColumn: "SiteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhpSettings_AccountId_Name",
                schema: "sites",
                table: "PhpSettings",
                columns: new[] { "AccountId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteBasicAuthUsers_SiteId_Name",
                schema: "sites",
                table: "SiteBasicAuthUsers",
                columns: new[] { "SiteId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteIpRules_SiteId_Position",
                schema: "sites",
                table: "SiteIpRules",
                columns: new[] { "SiteId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteRedirects_SiteId_Position",
                schema: "sites",
                table: "SiteRedirects",
                columns: new[] { "SiteId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PhpSettings", schema: "sites");
            migrationBuilder.DropTable(name: "SiteBasicAuthUsers", schema: "sites");
            migrationBuilder.DropTable(name: "SiteIpRules", schema: "sites");
            migrationBuilder.DropTable(name: "SiteRedirects", schema: "sites");
            migrationBuilder.DropTable(name: "SiteOptions", schema: "sites");

            migrationBuilder.DropTable(
                name: "SiteTrafficCursors",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "SiteTrafficDays",
                schema: "sites");

            migrationBuilder.DropColumn(
                name: "LogRetentionDays",
                schema: "sites",
                table: "Sites");
        }
    }
}
