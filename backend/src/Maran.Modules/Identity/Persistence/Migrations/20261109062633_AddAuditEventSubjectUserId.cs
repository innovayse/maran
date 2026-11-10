using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventSubjectUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubjectUserId",
                schema: "identity",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SubjectUserId",
                schema: "identity",
                table: "AuditEvents",
                column: "SubjectUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_SubjectUserId",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "SubjectUserId",
                schema: "identity",
                table: "AuditEvents");
        }
    }
}
