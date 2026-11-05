using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sftp.Persistence.Migrations
{
    /// <summary>
    /// Adds the one table SFTP public-key login needs: the keys a customer registered for a login.
    /// </summary>
    /// <remarks>
    /// Expand-only: a new table with a cascading foreign key to <c>SftpUsers</c>, nothing dropped,
    /// renamed or narrowed, so the previous release keeps running against the new schema. The key
    /// body is public material stored once in its validated one-line form; there is no private key
    /// and no secret in this table. The unique index on <c>SftpUserId, Fingerprint</c> is what makes
    /// "the same key twice on one login" a database fact rather than a handler's pre-check.
    /// </remarks>
    public partial class AddSftpUserKeys : Migration
    {
        /// <summary>The two columns of the per-login fingerprint index, hoisted out of the call.</summary>
        /// <remarks>
        /// A field rather than the inline array EF Core scaffolds, because CA1861 is an error in this
        /// repository (no <c>&lt;NoWarn&gt;</c>, rules/csharp.md).
        /// </remarks>
        private static readonly string[] SftpUserIdAndFingerprintColumns = ["SftpUserId", "Fingerprint"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SftpUserKeys",
                schema: "sftp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SftpUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SftpUserKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SftpUserKeys_SftpUsers_SftpUserId",
                        column: x => x.SftpUserId,
                        principalSchema: "sftp",
                        principalTable: "SftpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SftpUserKeys_SftpUserId_Fingerprint",
                schema: "sftp",
                table: "SftpUserKeys",
                columns: SftpUserIdAndFingerprintColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SftpUserKeys",
                schema: "sftp");
        }
    }
}
