using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelTasksFinishedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PanelTasks_FinishedAt",
                schema: "tasks",
                table: "PanelTasks",
                column: "FinishedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PanelTasks_FinishedAt",
                schema: "tasks",
                table: "PanelTasks");
        }
    }
}
