using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "accounts",
                table: "Plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_NameMirroredInKey",
                schema: "accounts",
                table: "Plans",
                sql: "\"Name\" IS NULL OR \"DisplayNameKey\" = \"Name\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_NameMirroredInKey",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "accounts",
                table: "Plans");
        }
    }
}
