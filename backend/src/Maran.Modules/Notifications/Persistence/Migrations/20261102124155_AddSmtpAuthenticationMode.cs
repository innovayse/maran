using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmtpAuthenticationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlertRecipientAddress",
                schema: "notifications",
                table: "SmtpSettings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthenticationMode",
                schema: "notifications",
                table: "SmtpSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BasicPassword",
                schema: "notifications",
                table: "SmtpSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BasicUsername",
                schema: "notifications",
                table: "SmtpSettings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            // raw-sql: copy the legacy columns into the typed ones; no parameters, no caller input. "Password" is
            // copied as stored (ciphertext): both columns use the same converter and key. A row with a non-empty
            // user name becomes basic and keeps its password; a row with none becomes anonymous.
            migrationBuilder.Sql("""
                UPDATE notifications."SmtpSettings"
                SET "AuthenticationMode" = CASE WHEN "Username" <> '' THEN 1 ELSE 0 END,
                    "BasicUsername"      = CASE WHEN "Username" <> '' THEN "Username" ELSE NULL END,
                    "BasicPassword"      = CASE WHEN "Username" <> '' THEN "Password" ELSE NULL END,
                    "AlertRecipientAddress" = NULLIF(btrim("AlertRecipient"), '');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmtpSettings_BasicCredentialsOnlyInBasicMode",
                schema: "notifications",
                table: "SmtpSettings",
                sql: "(\"AuthenticationMode\" = 0 AND \"BasicUsername\" IS NULL AND \"BasicPassword\" IS NULL) OR (\"AuthenticationMode\" = 1 AND \"BasicUsername\" IS NOT NULL AND \"BasicPassword\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SmtpSettings_BasicCredentialsOnlyInBasicMode",
                schema: "notifications",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "AlertRecipientAddress",
                schema: "notifications",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "AuthenticationMode",
                schema: "notifications",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "BasicPassword",
                schema: "notifications",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "BasicUsername",
                schema: "notifications",
                table: "SmtpSettings");
        }
    }
}
