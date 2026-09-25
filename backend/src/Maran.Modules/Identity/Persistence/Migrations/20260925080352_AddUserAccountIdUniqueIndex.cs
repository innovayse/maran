using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Safe against every row that predates this feature: <c>User.AccountId</c> had no writer
        /// before it, so every existing row is an administrator with a NULL there — and the partial
        /// filter below excludes NULLs from the uniqueness check. There is therefore no possible
        /// pre-existing violation for this index to fail on; only two rows both pointing at the SAME
        /// non-null account, which the code introduced alongside this migration is what starts
        /// writing this column at all, and it never lets that happen.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Users_AccountId",
                schema: "identity",
                table: "Users",
                column: "AccountId",
                unique: true,
                filter: "\"AccountId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Users_AccountId",
                schema: "identity",
                table: "Users");
        }
    }
}
