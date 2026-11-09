using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Mail.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMailSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mail");

            migrationBuilder.CreateTable(
                name: "MailServerSettings",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowExternalForwarding = table.Column<bool>(type: "boolean", nullable: false),
                    AntivirusEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    MaxMessageMib = table.Column<int>(type: "integer", nullable: false),
                    Pop3Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SpamAddHeaderScore = table.Column<int>(type: "integer", nullable: false),
                    SpamGreylistScore = table.Column<int>(type: "integer", nullable: false),
                    SpamRejectScore = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailServerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailDomains",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(189)", maxLength: 189, nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailDomains", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailDomains_AccountId",
                schema: "mail",
                table: "MailDomains",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MailDomains_Name",
                schema: "mail",
                table: "MailDomains",
                column: "Name",
                unique: true);

            migrationBuilder.CreateTable(
                name: "DkimKeys",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MailDomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivateKeyEncrypted = table.Column<string>(type: "text", nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Selector = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DkimKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DkimKeys_MailDomains_MailDomainId",
                        column: x => x.MailDomainId,
                        principalSchema: "mail",
                        principalTable: "MailDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DkimKeys_MailDomainId_Selector",
                schema: "mail",
                table: "DkimKeys",
                columns: new[] { "MailDomainId", "Selector" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "Mailboxes",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LocalPart = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MailDomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    QuotaMib = table.Column<int>(type: "integer", nullable: false),
                    SendLimitPerHour = table.Column<int>(type: "integer", nullable: false),
                    SpamAction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mailboxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mailboxes_MailDomains_MailDomainId",
                        column: x => x.MailDomainId,
                        principalSchema: "mail",
                        principalTable: "MailDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Mailboxes_AccountId",
                schema: "mail",
                table: "Mailboxes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Mailboxes_MailDomainId_LocalPart",
                schema: "mail",
                table: "Mailboxes",
                columns: new[] { "MailDomainId", "LocalPart" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "MailAliases",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocalPart = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MailDomainId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailAliases_MailDomains_MailDomainId",
                        column: x => x.MailDomainId,
                        principalSchema: "mail",
                        principalTable: "MailDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailAliases_AccountId",
                schema: "mail",
                table: "MailAliases",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MailAliases_MailDomainId_LocalPart",
                schema: "mail",
                table: "MailAliases",
                columns: new[] { "MailDomainId", "LocalPart" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "MailAliasTargets",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    MailAliasId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailAliasTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailAliasTargets_MailAliases_MailAliasId",
                        column: x => x.MailAliasId,
                        principalSchema: "mail",
                        principalTable: "MailAliases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailAliasTargets_MailAliasId_Position",
                schema: "mail",
                table: "MailAliasTargets",
                columns: new[] { "MailAliasId", "Position" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "MailAutoresponders",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MailboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplyIntervalDays = table.Column<int>(type: "integer", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailAutoresponders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailAutoresponders_Mailboxes_MailboxId",
                        column: x => x.MailboxId,
                        principalSchema: "mail",
                        principalTable: "Mailboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailAutoresponders_MailboxId",
                schema: "mail",
                table: "MailAutoresponders",
                column: "MailboxId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailAutoresponders",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "MailAliasTargets",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "MailAliases",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "Mailboxes",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "DkimKeys",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "MailDomains",
                schema: "mail");

            migrationBuilder.DropTable(
                name: "MailServerSettings",
                schema: "mail");
        }
    }
}
