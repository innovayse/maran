using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Sftp.Persistence.Migrations
{
    /// <summary>
    /// Creates the <c>sftp</c> schema and the one table in it: which account asked for which SFTP
    /// login, and what the host calls it.
    /// </summary>
    /// <remarks>
    /// Read the column list for what is absent. There is no password column of any kind — not
    /// plaintext, not encrypted, not a hash — and that is the design rather than an oversight: the
    /// panel mints a password, shows it once and forgets it, so the host's own shadow entry is the
    /// only copy in the system. A future migration adding one would give the panel's database the
    /// ability to hand over every customer's file-transfer credentials at once, and is a change that
    /// needs the reasoning in <c>SftpUser</c> answered first.
    ///
    /// There is no chroot path column either, and no protocol column. OpenSSH confines every login
    /// here with a fixed <c>ChrootDirectory %h</c> whose target the agent derives from the account
    /// name, so no customer names a directory and there is nothing to store; and this panel serves
    /// file transfer over <c>internal-sftp</c> alone, so a protocol column could hold one value.
    ///
    /// Two unique indexes, each stating a different fact. <c>AccountId, Name</c> is the customer's
    /// own namespace — two accounts may both have a <c>deploy</c>, which is exactly what the account
    /// prefix exists to allow. <c>FullName</c> is the host's namespace, which is global, and it
    /// exists because the handler's pre-insert check and its insert are not one atomic step.
    /// </remarks>
    public partial class InitialSftpSchema : Migration
    {
        /// <summary>The two columns of the per-account name index, hoisted out of the call.</summary>
        /// <remarks>
        /// A field rather than the inline array EF Core scaffolds, because CA1861 is an error in this
        /// repository (no <c>&lt;NoWarn&gt;</c>, rules/csharp.md). The scaffolded shape is otherwise
        /// unchanged; regenerating this migration reproduces the inline form and needs the same edit.
        /// </remarks>
        private static readonly string[] AccountIdAndNameColumns = ["AccountId", "Name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sftp");

            migrationBuilder.CreateTable(
                name: "SftpUsers",
                schema: "sftp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SftpUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SftpUsers_AccountId",
                schema: "sftp",
                table: "SftpUsers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SftpUsers_AccountId_Name",
                schema: "sftp",
                table: "SftpUsers",
                columns: AccountIdAndNameColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SftpUsers_FullName",
                schema: "sftp",
                table: "SftpUsers",
                column: "FullName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SftpUsers",
                schema: "sftp");
        }
    }
}
