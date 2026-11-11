using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Apps.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppLedgerConcurrency : Migration
    {
        /// <summary>
        /// Marks every active web server row but one as failed (State 3 is Failed; 1, 2 and 4 are the states that
        /// hold the ports: Installing, Installed, Removing). Public so a test can pin that it runs before the index.
        /// </summary>
        public const string ResolveDuplicateWebServers =
            "UPDATE apps.\"InstalledApps\" SET \"State\" = 3, \"TaskId\" = NULL, \"Revision\" = \"Revision\" + 1 "
            + "WHERE \"Kind\" = 1 AND \"State\" IN (1, 2, 4) AND \"Id\" NOT IN ("
            + "SELECT \"Id\" FROM apps.\"InstalledApps\" WHERE \"Kind\" = 1 AND \"State\" IN (1, 2, 4) "
            + "ORDER BY (\"State\" = 2) DESC, \"UpdatedAt\" DESC, \"Id\" LIMIT 1);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "apps",
                table: "InstalledApps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The task id now names only the operation in flight (issue #524): a row that is installed or
            // failed carries none, and the rows written before this release still carry their last one.
            migrationBuilder.Sql(
                "UPDATE apps.\"InstalledApps\" SET \"TaskId\" = NULL WHERE \"State\" IN (2, 3);");

            // A ledger written before this release may hold two web servers that each hold the ports (two
            // installs that raced), and the unique index below cannot be created over them. Keep one,
            // deterministically: the installed one if there is one (an installed row is what the host was
            // last seen to have), otherwise the most recently changed, otherwise the lowest id; mark the
            // others failed, which holds no place. The start-up pass settles the survivors against the host.
            migrationBuilder.Sql(ResolveDuplicateWebServers);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledApps_ActiveWebServer",
                schema: "apps",
                table: "InstalledApps",
                column: "Kind",
                unique: true,
                filter: "\"Kind\" = 1 AND \"State\" IN (1, 2, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstalledApps_ActiveWebServer",
                schema: "apps",
                table: "InstalledApps");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "apps",
                table: "InstalledApps");
        }
    }
}
