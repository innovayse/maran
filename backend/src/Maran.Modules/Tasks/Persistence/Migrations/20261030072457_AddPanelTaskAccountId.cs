using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelTaskAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                schema: "tasks",
                table: "PanelTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                schema: "tasks",
                table: "PanelTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "AdministratorOnly");

            migrationBuilder.CreateIndex(
                name: "IX_PanelTasks_AccountId",
                schema: "tasks",
                table: "PanelTasks",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PanelTasks_AccountId",
                schema: "tasks",
                table: "PanelTasks");

            migrationBuilder.DropColumn(
                name: "AccountId",
                schema: "tasks",
                table: "PanelTasks");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "tasks",
                table: "PanelTasks");
        }
    }
}
