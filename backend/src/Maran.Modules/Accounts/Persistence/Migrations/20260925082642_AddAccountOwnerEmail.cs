using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <summary>
    /// Adds the account's own contact address (<c>Account.OwnerEmail</c>), so an outstanding
    /// invitation can be recovered even when the login that would otherwise be its only home was
    /// never created (see <c>Account.OwnerEmail</c>'s own remarks).
    /// </summary>
    /// <remarks>
    /// Nullable, and left untouched for every row that predates this column: nothing in the panel
    /// ever recorded an existing account's owner address anywhere durable — it lived only in the
    /// non-persisted <see cref="Maran.Sdk.Events.AccountCreated"/> event — so there is nothing to
    /// backfill from, and a null here is honestly "unknown" rather than a guessed value. A new
    /// account always writes a real one; <c>CreateAccountCommandValidator</c> requires it.
    /// </remarks>
    public partial class AddAccountOwnerEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerEmail",
                schema: "accounts",
                table: "Accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerEmail",
                schema: "accounts",
                table: "Accounts");
        }
    }
}
