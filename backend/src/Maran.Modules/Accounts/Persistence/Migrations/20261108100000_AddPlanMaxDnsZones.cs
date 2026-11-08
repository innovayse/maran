using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanMaxDnsZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only (issue #178): one NOT NULL column whose default is Plan.DefaultMaxDnsZones, so every
            // plan that exists before this release reads as ten, and the previous release, which ignores the
            // column, keeps working against the new schema.
            migrationBuilder.AddColumn<int>(
                name: "MaxDnsZones",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxDnsZones",
                schema: "accounts",
                table: "Plans");
        }
    }
}
