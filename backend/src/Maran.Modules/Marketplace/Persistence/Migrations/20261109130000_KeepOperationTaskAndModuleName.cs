using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Marketplace.Persistence.Migrations
{
    /// <summary>
    /// Expand-only: the operation claim names its operation, version and task, and an installed package
    /// keeps the catalogue's name for the module. Every new column is nullable, so a row written by the
    /// previous release stays valid and the previous release ignores the columns.
    /// </summary>
    public partial class KeepOperationTaskAndModuleName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "marketplace",
                table: "InstalledPackages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "marketplace",
                table: "ModuleOperationLocks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskId",
                schema: "marketplace",
                table: "ModuleOperationLocks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                schema: "marketplace",
                table: "ModuleOperationLocks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "marketplace",
                table: "InstalledPackages");

            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "marketplace",
                table: "ModuleOperationLocks");

            migrationBuilder.DropColumn(
                name: "TaskId",
                schema: "marketplace",
                table: "ModuleOperationLocks");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "marketplace",
                table: "ModuleOperationLocks");
        }
    }
}
