using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipsPinsAndViewAs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveAccountId",
                schema: "identity",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorSessionId",
                schema: "identity",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                schema: "identity",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonatorUserId",
                schema: "identity",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImpersonatorUsername",
                schema: "identity",
                table: "AuditEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountMemberships",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Grants = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PinnedPages",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    PinnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PinnedPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PinnedPages_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMemberships_AccountId",
                schema: "identity",
                table: "AccountMemberships",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountMemberships_UserId_AccountId",
                schema: "identity",
                table: "AccountMemberships",
                columns: new[] { "UserId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PinnedPages_UserId_ItemKey",
                schema: "identity",
                table: "PinnedPages",
                columns: new[] { "UserId", "ItemKey" },
                unique: true);

            // raw-sql: one-time expand backfill, parameter-free; every login that owns an account
            // through the legacy Users.AccountId column receives its Owner membership, so the
            // memberships table is the truth from this release on (issue #313). Grants are 0
            // because an Owner is not restricted by grants (AccountMembership.Allows).
            migrationBuilder.Sql(
                """
                INSERT INTO identity."AccountMemberships" ("Id", "UserId", "AccountId", "Role", "Grants", "CreatedAt")
                SELECT gen_random_uuid(), u."Id", u."AccountId", 'Owner', 0, u."CreatedAt"
                FROM identity."Users" AS u
                WHERE u."AccountId" IS NOT NULL
                ON CONFLICT ("UserId", "AccountId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountMemberships",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "PinnedPages",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "ActiveAccountId",
                schema: "identity",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ActorSessionId",
                schema: "identity",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                schema: "identity",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ImpersonatorUserId",
                schema: "identity",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "ImpersonatorUsername",
                schema: "identity",
                table: "AuditEvents");
        }
    }
}
