using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ssl.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateRenewalFailureCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastRenewalFailureCode",
                schema: "ssl",
                table: "Certificates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // raw-sql: copy the legacy empty-string spelling of "no failure" into the nullable column.
            // Idempotent and parameter-free; the legacy column stays written.
            migrationBuilder.Sql("""
                UPDATE ssl."Certificates"
                SET "LastRenewalFailureCode" = NULLIF("LastRenewalErrorCode", '')
                WHERE "LastRenewalFailureCode" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRenewalFailureCode",
                schema: "ssl",
                table: "Certificates");
        }
    }
}
