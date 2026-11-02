using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanResourceCeilings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CpuQuotaPercentCeiling",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxProcessesCeiling",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MemoryMaxMbCeiling",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: true);

            // raw-sql: copy each previous-release (kind, figure) pair into its nullable ceiling; an unlimited
            // ceiling (and its stored zero) becomes NULL. Parameter-free; the old pair is left untouched.
            migrationBuilder.Sql("""
                UPDATE accounts."Plans" SET
                    "CpuQuotaPercentCeiling" = CASE WHEN "CpuLimitKind" = 'Limited' THEN "CpuQuotaPercent" END,
                    "MemoryMaxMbCeiling"     = CASE WHEN "MemoryLimitKind" = 'Limited' THEN "MemoryMaxMb" END,
                    "MaxProcessesCeiling"    = CASE WHEN "ProcessLimitKind" = 'Limited' THEN "MaxProcesses" END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuQuotaPercentCeiling",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxProcessesCeiling",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MemoryMaxMbCeiling",
                schema: "accounts",
                table: "Plans");
        }
    }
}
