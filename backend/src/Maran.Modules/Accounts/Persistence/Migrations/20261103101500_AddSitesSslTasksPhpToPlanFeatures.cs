using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSitesSslTasksPhpToPlanFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // raw-sql: add the four feature bits issue #340 gates (Sites 64, Ssl 128, Tasks 256, Php 512 =
            // 960) to every stored plan. Those four were never gated before, so every plan already sold
            // them in effect; without this a plan stored as the old full set (63) would stop selling
            // websites, certificates, tasks and PHP the moment the new filters ship. Parameter-free,
            // additive (bits are only set), and the previous release ignores bits it does not know.
            migrationBuilder.Sql("""
                UPDATE accounts."Plans" SET "Features" = "Features" | 960;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // raw-sql: clear the four bits again; the column and every other feature are untouched.
            migrationBuilder.Sql("""
                UPDATE accounts."Plans" SET "Features" = "Features" & ~960;
                """);
        }
    }
}
