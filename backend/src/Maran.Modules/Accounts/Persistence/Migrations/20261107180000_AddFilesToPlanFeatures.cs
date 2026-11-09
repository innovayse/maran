using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilesToPlanFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // raw-sql: add the feature bit the file manager is gated by (Files 1024) to every stored plan.
            // The file manager is new, so no plan sold it before; but every shipped plan is the full set,
            // and a plan stored as the old full set (1023) would stop selling it the moment the Files
            // controllers ship. Parameter-free, additive (the bit is only set), and the previous release
            // ignores bits it does not know.
            migrationBuilder.Sql("""
                UPDATE accounts."Plans" SET "Features" = "Features" | 1024;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // raw-sql: clear the one bit again; the column and every other feature are untouched.
            migrationBuilder.Sql("""
                UPDATE accounts."Plans" SET "Features" = "Features" & ~1024;
                """);
        }
    }
}
