using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Databases.Persistence.Migrations
{
    /// <summary>
    /// Adds the two tables that make a database user a thing of its own: <c>DatabaseUsers</c>, one row
    /// per MySQL login an account owns, and <c>DatabaseGrants</c>, what each login holds on each database.
    /// </summary>
    /// <remarks>
    /// Expand only: two new tables, nothing dropped, renamed or narrowed, so the previous release still
    /// runs against this schema. The existing <c>Databases</c> table is untouched and its
    /// <c>DbUserNameSuffix</c> stays the pointer that makes a user a database's dedicated one. The rows
    /// for databases created before this migration are written by a start-up pass, not by SQL here, so a
    /// roll-back and roll-forward through a release that creates databases without user rows is repaired
    /// by the next start. There is no password column, for the reason the first migration gives.
    /// </remarks>
    public partial class AddDatabaseUsers : Migration
    {
        /// <summary>The two columns of the per-user, per-database unique index, hoisted out of the call (CA1861).</summary>
        private static readonly string[] UserAndDatabaseColumns = ["DatabaseUserId", "DatabaseId"];

        /// <summary>The two columns of the per-account name unique index, hoisted out of the call (CA1861).</summary>
        private static readonly string[] AccountIdAndNameSuffixColumns = ["AccountId", "NameSuffix"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseUsers",
                schema: "databases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameSuffix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseGrants",
                schema: "databases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatabaseUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatabaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Privileges = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatabaseGrants_DatabaseUsers_DatabaseUserId",
                        column: x => x.DatabaseUserId,
                        principalSchema: "databases",
                        principalTable: "DatabaseUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatabaseGrants_Databases_DatabaseId",
                        column: x => x.DatabaseId,
                        principalSchema: "databases",
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseGrants_DatabaseId",
                schema: "databases",
                table: "DatabaseGrants",
                column: "DatabaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseGrants_DatabaseUserId_DatabaseId",
                schema: "databases",
                table: "DatabaseGrants",
                columns: UserAndDatabaseColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseUsers_AccountId_NameSuffix",
                schema: "databases",
                table: "DatabaseUsers",
                columns: AccountIdAndNameSuffixColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseUsers_FullName",
                schema: "databases",
                table: "DatabaseUsers",
                column: "FullName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseGrants",
                schema: "databases");

            migrationBuilder.DropTable(
                name: "DatabaseUsers",
                schema: "databases");
        }
    }
}
