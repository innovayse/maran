using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Features",
                schema: "accounts",
                table: "Plans",
                type: "bigint",
                nullable: false,
                defaultValue: 63L); // AccountFeature.All: existing and shipped plans keep every feature (#316)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Features",
                schema: "accounts",
                table: "Plans");
        }
    }
}
