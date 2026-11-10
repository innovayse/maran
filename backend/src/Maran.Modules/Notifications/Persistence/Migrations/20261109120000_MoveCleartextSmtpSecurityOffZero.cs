using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maran.Modules.Notifications.Persistence.Migrations
{
    /// <summary>
    /// Moves the stored cleartext mode off the value 0 (issue #491): <c>SmtpSecurity.None</c> is now 3
    /// and 0 names no mode, so a request that omits the field cannot select it.
    /// </summary>
    /// <remarks>
    /// A data-only migration: the column, its type and the model are unchanged, so the snapshot does
    /// not move. A row that held 0 held the cleartext mode and keeps it, as 3; the rows holding 1
    /// (STARTTLS) and 2 (implicit TLS) are untouched. Rolling the application back needs the Down
    /// step first, which restores the old number; without it the previous release reads 3 as an
    /// unrecognised mode and falls back to STARTTLS, the stricter of the encrypted modes, so a
    /// localhost relay that takes no TLS stops receiving mail until the setting is saved again. It
    /// never downgrades to cleartext.
    /// </remarks>
    public partial class MoveCleartextSmtpSecurityOffZero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // raw-sql: rewrites the stored cleartext mode from its old number to its new one; no
            // parameters, no caller input. Idempotent: a second run finds no row holding 0.
            migrationBuilder.Sql("""
                UPDATE notifications."SmtpSettings"
                SET "Security" = 3
                WHERE "Security" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // raw-sql: the inverse rewrite, so a rolled-back application finds the number it reads.
            migrationBuilder.Sql("""
                UPDATE notifications."SmtpSettings"
                SET "Security" = 0
                WHERE "Security" = 3;
                """);
        }
    }
}
