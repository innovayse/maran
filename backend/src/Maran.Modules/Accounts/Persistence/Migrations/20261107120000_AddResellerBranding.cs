using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResellerBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResellerBrandings",
                schema: "accounts",
                columns: table => new
                {
                    ResellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Logo = table.Column<string>(type: "character varying(140000)", maxLength: 140000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerBrandings", x => x.ResellerId);
                    table.ForeignKey(
                        name: "FK_ResellerBrandings_Resellers_ResellerId",
                        column: x => x.ResellerId,
                        principalSchema: "accounts",
                        principalTable: "Resellers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResellerBrandings",
                schema: "accounts");
        }
    }
}
