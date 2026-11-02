using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <summary>
    /// Adds the account owner's language (<c>Account.OwnerLanguage</c>), the language the owner's
    /// invitation is written in (issue #194).
    /// </summary>
    /// <remarks>
    /// Expand only: a required column with a database default, so every existing row reads
    /// <c>en</c> — English is the one language every installation ships — and the previous release,
    /// which does not know the column, keeps inserting rows that the default completes.
    /// </remarks>
    public partial class AddAccountOwnerLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerLanguage",
                schema: "accounts",
                table: "Accounts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerLanguage",
                schema: "accounts",
                table: "Accounts");
        }
    }
}
