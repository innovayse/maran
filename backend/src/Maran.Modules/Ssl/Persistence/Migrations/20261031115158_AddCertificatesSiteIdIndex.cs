using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ssl.Persistence.Migrations
{
    /// <summary>
    /// Indexes a certificate's site, so the list's site filter reads the one row it asks for instead
    /// of every certificate on the server (issue #196, docs/performance/list-indexes.md). Additive:
    /// the previous release reads nothing this changes.
    /// </summary>
    public partial class AddCertificatesSiteIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Certificates_SiteId",
                schema: "ssl",
                table: "Certificates",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Certificates_SiteId",
                schema: "ssl",
                table: "Certificates");
        }
    }
}
