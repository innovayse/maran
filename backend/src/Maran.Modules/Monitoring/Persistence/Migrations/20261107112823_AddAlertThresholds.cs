using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Monitoring.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertThresholds",
                schema: "monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiskPercent = table.Column<int>(type: "integer", nullable: false),
                    CpuPercent = table.Column<int>(type: "integer", nullable: true),
                    MemoryPercent = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertThresholds", x => x.Id);
                    table.CheckConstraint("CK_AlertThresholds_CpuPercent", "\"CpuPercent\" IS NULL OR \"CpuPercent\" BETWEEN 50 AND 99");
                    table.CheckConstraint("CK_AlertThresholds_DiskPercent", "\"DiskPercent\" BETWEEN 50 AND 99");
                    table.CheckConstraint("CK_AlertThresholds_MemoryPercent", "\"MemoryPercent\" IS NULL OR \"MemoryPercent\" BETWEEN 50 AND 99");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertThresholds",
                schema: "monitoring");
        }
    }
}
