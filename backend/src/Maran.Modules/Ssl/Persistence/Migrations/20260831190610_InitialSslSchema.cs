using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Ssl.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSslSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ssl");

            migrationBuilder.CreateTable(
                name: "AcmeAccounts",
                schema: "ssl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectoryUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AccountUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PrivateKeyPem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcmeAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                schema: "ssl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NotAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRenewalAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRenewalErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConsecutiveRenewalFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcmeAccounts_DirectoryUrl",
                schema: "ssl",
                table: "AcmeAccounts",
                column: "DirectoryUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_AccountId",
                schema: "ssl",
                table: "Certificates",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_Domain",
                schema: "ssl",
                table: "Certificates",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_NotAfter",
                schema: "ssl",
                table: "Certificates",
                column: "NotAfter");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcmeAccounts",
                schema: "ssl");

            migrationBuilder.DropTable(
                name: "Certificates",
                schema: "ssl");
        }
    }
}
