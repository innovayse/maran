using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sites.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteHostnameClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteHostnames",
                schema: "sites",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHostnames", x => x.Name);
                    table.ForeignKey(
                        name: "FK_SiteHostnames_Sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteHostnames_SiteId",
                schema: "sites",
                table: "SiteHostnames",
                column: "SiteId");

            // Sites that already exist claimed their names before this table did the deciding, so
            // without this backfill their domains and aliases would read as unclaimed and another
            // account could take them. Where two existing sites already share a name — which only
            // an alias collision predating this migration can produce — the first row wins the
            // claim and the operator resolves the duplicate; refusing to migrate would leave the
            // server with no uniqueness at all, which is the worse of the two.
            // raw-sql: a set-based backfill over every row; no user input reaches it and it takes
            // no parameters, so there is nothing here for a parameterized query to carry.
            migrationBuilder.Sql(
                """
                INSERT INTO sites."SiteHostnames" ("Name", "SiteId")
                SELECT DISTINCT lower(hostname), site."Id"
                FROM sites."Sites" AS site
                CROSS JOIN LATERAL unnest(array_append(site."Aliases", site."Domain")) AS hostname
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteHostnames",
                schema: "sites");
        }
    }
}
