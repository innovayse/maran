using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelTaskStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PanelTasks_Status",
                schema: "tasks",
                table: "PanelTasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PanelTasks_Status",
                schema: "tasks",
                table: "PanelTasks");
        }
    }
}
