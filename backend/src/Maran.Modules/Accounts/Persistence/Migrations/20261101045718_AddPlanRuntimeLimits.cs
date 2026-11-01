using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanRuntimeLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every plan that exists before this release reads as unlimited on all three runtime
            // ceilings (issue #133), which is exactly what the host enforced for it; the figure
            // columns hold 0, which Plan.RuntimeLimits reads only behind a Limited kind.
            migrationBuilder.AddColumn<string>(
                name: "CpuLimitKind",
                schema: "accounts",
                table: "Plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unlimited");

            migrationBuilder.AddColumn<int>(
                name: "CpuQuotaPercent",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxProcesses",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MemoryLimitKind",
                schema: "accounts",
                table: "Plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unlimited");

            migrationBuilder.AddColumn<int>(
                name: "MemoryMaxMb",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProcessLimitKind",
                schema: "accounts",
                table: "Plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unlimited");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuLimitKind",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CpuQuotaPercent",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxProcesses",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MemoryLimitKind",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MemoryMaxMb",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ProcessLimitKind",
                schema: "accounts",
                table: "Plans");
        }
    }
}
