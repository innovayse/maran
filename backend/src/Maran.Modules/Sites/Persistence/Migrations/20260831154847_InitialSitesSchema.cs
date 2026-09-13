using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sites.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSitesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sites");

            migrationBuilder.CreateTable(
                name: "Sites",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    BackendType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PhpVersion = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProxyUpstream = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    DocumentRoot = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    HasCertificate = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_AccountId",
                schema: "sites",
                table: "Sites",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Domain",
                schema: "sites",
                table: "Sites",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sites",
                schema: "sites");
        }
    }
}
