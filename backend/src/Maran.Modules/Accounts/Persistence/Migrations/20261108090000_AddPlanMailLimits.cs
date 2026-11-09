using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Accounts.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanMailLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand only (issue #177): six NOT NULL columns, each with a database default, so every row that
            // exists before this release reads as the default allowance and the previous release, which
            // ignores the columns, keeps working against the new schema. The four on Plans equal
            // MailLimits.Default (10 mailboxes, 1024 MiB each, 100 messages an hour, a 10240 MiB pool); the two
            // on Resellers are the envelope ceilings, set to the same mailbox count and pool so a plan that
            // states no mail figures still fits every reseller that existed before mail.
            migrationBuilder.AddColumn<int>(
                name: "MaxMailboxes",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "DefaultMailboxQuotaMib",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 1024);

            migrationBuilder.AddColumn<int>(
                name: "MailboxSendLimitPerHour",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "MailboxQuotaPoolMib",
                schema: "accounts",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 10240);

            migrationBuilder.AddColumn<int>(
                name: "MaxMailboxes",
                schema: "accounts",
                table: "Resellers",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "MailboxQuotaPoolMib",
                schema: "accounts",
                table: "Resellers",
                type: "integer",
                nullable: false,
                defaultValue: 10240);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxMailboxes",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "DefaultMailboxQuotaMib",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MailboxSendLimitPerHour",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MailboxQuotaPoolMib",
                schema: "accounts",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxMailboxes",
                schema: "accounts",
                table: "Resellers");

            migrationBuilder.DropColumn(
                name: "MailboxQuotaPoolMib",
                schema: "accounts",
                table: "Resellers");
        }
    }
}
