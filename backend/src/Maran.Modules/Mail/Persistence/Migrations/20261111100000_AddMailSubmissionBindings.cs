using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Mail.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailSubmissionBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailSubmissionBindings",
                schema: "mail",
                columns: table => new
                {
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailSubmissionBindings", x => x.Username);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailSubmissionBindings",
                schema: "mail");
        }
    }
}
