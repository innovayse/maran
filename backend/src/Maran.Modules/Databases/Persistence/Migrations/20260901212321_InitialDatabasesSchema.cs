using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Databases.Persistence.Migrations
{
    /// <summary>
    /// Creates the <c>databases</c> schema and the one table in it: which account asked for which
    /// MySQL database, what the server calls it, and which dedicated user goes with it.
    /// </summary>
    /// <remarks>
    /// Read the column list for what is absent. There is no password column of any kind — not
    /// plaintext, not encrypted, not a hash — and that is the design rather than an oversight: the
    /// panel mints a password, shows it once and forgets it, so the server's own hash is the only
    /// copy in the system. A future migration adding one would give the panel's database the ability
    /// to hand over every customer's credentials at once, and is a change that needs the reasoning in
    /// <c>Database</c> and <c>db.proto</c> answered first.
    ///
    /// Three unique indexes, each stating a different fact. <c>AccountId, Name</c> is the customer's
    /// own namespace — two accounts may both have a <c>shop</c>, which is exactly what the account
    /// prefix exists to allow. <c>FullName</c> and <c>DbUserName</c> are MySQL's namespaces, which
    /// are server-wide, and they exist because the handler's pre-insert check and its insert are not
    /// one atomic step.
    /// </remarks>
    public partial class InitialDatabasesSchema : Migration
    {
        /// <summary>The two columns of the per-account name index, hoisted out of the call.</summary>
        /// <remarks>
        /// A field rather than the inline array EF Core scaffolds, because CA1861 is an error in this
        /// repository (no <c>&lt;NoWarn&gt;</c>, rules/csharp.md). The scaffolded shape is otherwise
        /// unchanged; regenerating this migration reproduces the inline form and needs the same edit.
        /// </remarks>
        private static readonly string[] AccountIdAndNameColumns = ["AccountId", "Name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "databases");

            migrationBuilder.CreateTable(
                name: "Databases",
                schema: "databases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FullName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DbUserName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DbUserNameSuffix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Databases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Databases_AccountId",
                schema: "databases",
                table: "Databases",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Databases_AccountId_Name",
                schema: "databases",
                table: "Databases",
                columns: AccountIdAndNameColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Databases_DbUserName",
                schema: "databases",
                table: "Databases",
                column: "DbUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Databases_FullName",
                schema: "databases",
                table: "Databases",
                column: "FullName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Databases",
                schema: "databases");
        }
    }
}
