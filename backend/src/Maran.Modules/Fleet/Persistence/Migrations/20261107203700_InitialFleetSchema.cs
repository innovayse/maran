using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Fleet.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFleetSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fleet");

            migrationBuilder.CreateTable(
                name: "Servers",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Servers_Fingerprint",
                schema: "fleet",
                table: "Servers",
                column: "Fingerprint",
                unique: true,
                filter: "\"Fingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_Name",
                schema: "fleet",
                table: "Servers",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Servers",
                schema: "fleet");
        }
    }
}
