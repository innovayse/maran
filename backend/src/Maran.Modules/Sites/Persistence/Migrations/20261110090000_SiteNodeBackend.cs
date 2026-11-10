using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sites.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteNodeBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only: three added columns, each NOT NULL with a default so that the previous
            // release, which inserts sites without knowing them, keeps working against the new schema and
            // every existing row reads as "not a Node site".
            migrationBuilder.AddColumn<int>(
                name: "AppPort",
                schema: "sites",
                table: "Sites",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NodeVersion",
                schema: "sites",
                table: "Sites",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StartCommand",
                schema: "sites",
                table: "Sites",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppPort",
                schema: "sites",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "NodeVersion",
                schema: "sites",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "StartCommand",
                schema: "sites",
                table: "Sites");
        }
    }
}
