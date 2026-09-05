using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityPolicyAndPasswordResetTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityPolicy",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumPasswordLength = table.Column<int>(type: "integer", nullable: false),
                    ForceTwoFactorForAdmins = table.Column<bool>(type: "boolean", nullable: false),
                    MaxFailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPolicy", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                schema: "identity",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                schema: "identity",
                table: "PasswordResetTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SecurityPolicy",
                schema: "identity");
        }
    }
}
