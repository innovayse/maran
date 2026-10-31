using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Monitoring.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitDiskReserve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiskInUseBytes",
                schema: "monitoring",
                table: "Samples",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiskInUseBytes",
                schema: "monitoring",
                table: "Samples");
        }
    }
}
