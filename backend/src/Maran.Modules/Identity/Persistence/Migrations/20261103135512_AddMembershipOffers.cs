using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountMembershipOffers",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Grants = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountMembershipOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountMembershipOffers_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMembershipOffers_AccountId",
                schema: "identity",
                table: "AccountMembershipOffers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountMembershipOffers_UserId",
                schema: "identity",
                table: "AccountMembershipOffers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountMembershipOffers_UserId_AccountId_Open",
                schema: "identity",
                table: "AccountMembershipOffers",
                columns: new[] { "UserId", "AccountId" },
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountMembershipOffers",
                schema: "identity");
        }
    }
}
