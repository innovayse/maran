using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sites.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteChildRowsReferenceTheirSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestRateBurst",
                schema: "sites",
                table: "SiteOptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestRatePerSecond",
                schema: "sites",
                table: "SiteOptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteOptions_RequestRate",
                schema: "sites",
                table: "SiteOptions",
                sql: "(\"RequestRatePerSecond\" IS NULL) = (\"RequestRateBurst\" IS NULL)");

            // Expand only: three added foreign keys, two added nullable columns and a check over them, and
            // nothing the previous release reads is dropped, renamed or narrowed. The new columns start
            // null, so the check holds for every existing row. No row can violate a key, so nothing is
            // deleted first: each of the three key columns already holds a SiteOptions key
            // (FK_*_SiteOptions_SiteId), and SiteOptions.SiteId is itself a foreign key to Sites.Id
            // (FK_SiteOptions_Sites_SiteId), so every existing row names a site that exists. No index is added: each table has one that leads with SiteId.
            migrationBuilder.AddForeignKey(
                name: "FK_SiteBasicAuthUsers_Sites_SiteId",
                schema: "sites",
                table: "SiteBasicAuthUsers",
                column: "SiteId",
                principalSchema: "sites",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteIpRules_Sites_SiteId",
                schema: "sites",
                table: "SiteIpRules",
                column: "SiteId",
                principalSchema: "sites",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteRedirects_Sites_SiteId",
                schema: "sites",
                table: "SiteRedirects",
                column: "SiteId",
                principalSchema: "sites",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteBasicAuthUsers_Sites_SiteId",
                schema: "sites",
                table: "SiteBasicAuthUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_SiteIpRules_Sites_SiteId",
                schema: "sites",
                table: "SiteIpRules");

            migrationBuilder.DropForeignKey(
                name: "FK_SiteRedirects_Sites_SiteId",
                schema: "sites",
                table: "SiteRedirects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteOptions_RequestRate",
                schema: "sites",
                table: "SiteOptions");

            migrationBuilder.DropColumn(
                name: "RequestRateBurst",
                schema: "sites",
                table: "SiteOptions");

            migrationBuilder.DropColumn(
                name: "RequestRatePerSecond",
                schema: "sites",
                table: "SiteOptions");
        }
    }
}
