using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ssl.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateIsWildcard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWildcard",
                schema: "ssl",
                table: "Certificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsWildcard",
                schema: "ssl",
                table: "Certificates");
        }
    }
}
