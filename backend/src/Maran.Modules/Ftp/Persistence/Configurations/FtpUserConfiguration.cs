using Maran.Modules.Ftp.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Ftp.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="FtpUser"/> onto the <c>ftp.FtpUsers</c> table.</summary>
/// <remarks>
/// Read the column list for what is NOT here: there is no password column of any kind, and no jail
/// path. Both absences are the design (see <see cref="FtpUser"/>), and the first of them is asserted
/// by a test rather than left to a reviewer noticing a new property one day.
/// </remarks>
public sealed class FtpUserConfiguration : IEntityTypeConfiguration<FtpUser>
{
    /// <summary>The <c>useradd</c> name ceiling, which a prefixed login cannot exceed.</summary>
    /// <remarks>
    /// Thirty-two bytes, the limit the agent's own <c>FtpsUserName</c> enforces on both supported
    /// distribution families. The column is sized to it so the database cannot hold a name the host
    /// would have refused.
    /// </remarks>
    private const int SystemUserNameMaxLength = 32;

    /// <summary>Configures the table, keys, and column constraints for <see cref="FtpUser"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<FtpUser> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("FtpUsers");
        builder.HasKey(ftpUser => ftpUser.Id);

        builder.Property(ftpUser => ftpUser.AccountId)
            .IsRequired();

        builder.Property(ftpUser => ftpUser.Name)
            .IsRequired()
            .HasMaxLength(SystemUserNameMaxLength);

        builder.Property(ftpUser => ftpUser.FullName)
            .IsRequired()
            .HasMaxLength(SystemUserNameMaxLength);

        builder.Property(ftpUser => ftpUser.CreatedAt)
            .IsRequired();

        // The name the CUSTOMER chose is unique within their account and deliberately not across the
        // host: the account prefix is what lets two customers both have a `files`. Scoping it to the
        // whole host instead would hand the first tenant to ask a name every other tenant then could
        // never use, which is the problem the prefix exists to solve.
        builder.HasIndex(ftpUser => new { ftpUser.AccountId, ftpUser.Name })
            .IsUnique()
            .HasDatabaseName("IX_FtpUsers_AccountId_Name");

        // The host's user namespace IS global, so the prefixed login is unique across every account.
        // Enforced here as well as by the pre-insert check, because that check and the insert are not
        // one atomic step: two simultaneous creations of the same name must not both produce a row,
        // and the loser must arrive as a typed conflict rather than as two rows claiming one login.
        //
        // What it does NOT enforce, stated so nobody reads more into it than is there: the PLAN
        // LIMIT. A unique index refuses a repeated VALUE and the allowance is a CARDINALITY, so "at
        // most N rows for this account" cannot be expressed here at all. What closes that gap is not
        // in this file: `IFtpUserSlotGate` re-counts and inserts under a per-account advisory lock,
        // and it carries the argument for why each alternative to a lock — including an index —
        // cannot see the other transaction's uncommitted row. An earlier version of this comment
        // said the limit "stays a count-then-insert check in the handler" whose race was "accepted";
        // that stopped being true when the gate landed, and a comment saying a hole is accepted is
        // what stops the next reader checking whether it is still open.
        builder.HasIndex(ftpUser => ftpUser.FullName)
            .IsUnique()
            .HasDatabaseName("IX_FtpUsers_FullName");

        // Every tenant-scoped read is "this account's logins", which is the query the global filter
        // emits on every single request from a customer — and it is also the read the plan-limit
        // count makes on every creation.
        builder.HasIndex(ftpUser => ftpUser.AccountId).HasDatabaseName("IX_FtpUsers_AccountId");
    }
}
