using Maran.Modules.Sftp.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sftp.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SftpUser"/> onto the <c>sftp.SftpUsers</c> table.</summary>
/// <remarks>
/// Read the column list for what is NOT here: there is no password column of any kind, and no chroot
/// path. Both absences are the design (see <see cref="SftpUser"/>), and the first of them is
/// asserted by a test rather than left to a reviewer noticing a new property one day.
/// </remarks>
public sealed class SftpUserConfiguration : IEntityTypeConfiguration<SftpUser>
{
    /// <summary>The <c>useradd</c> name ceiling, which a prefixed login cannot exceed.</summary>
    /// <remarks>
    /// Thirty-two bytes, the limit the agent's own <c>SftpUserName</c> enforces on both supported
    /// distribution families. The column is sized to it so the database cannot hold a name the host
    /// would have refused.
    /// </remarks>
    private const int SystemUserNameMaxLength = 32;

    /// <summary>Configures the table, keys, and column constraints for <see cref="SftpUser"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SftpUser> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("SftpUsers");
        builder.HasKey(sftpUser => sftpUser.Id);

        builder.Property(sftpUser => sftpUser.AccountId)
            .IsRequired();

        builder.Property(sftpUser => sftpUser.Name)
            .IsRequired()
            .HasMaxLength(SystemUserNameMaxLength);

        builder.Property(sftpUser => sftpUser.FullName)
            .IsRequired()
            .HasMaxLength(SystemUserNameMaxLength);

        builder.Property(sftpUser => sftpUser.CreatedAt)
            .IsRequired();

        // The name the CUSTOMER chose is unique within their account and deliberately not across the
        // host: the account prefix is what lets two customers both have a `deploy`. Scoping it to
        // the whole host instead would hand the first tenant to ask a name every other tenant then
        // could never use, which is the problem the prefix exists to solve.
        builder.HasIndex(sftpUser => new { sftpUser.AccountId, sftpUser.Name })
            .IsUnique()
            .HasDatabaseName("IX_SftpUsers_AccountId_Name");

        // The host's user namespace IS global, so the prefixed login is unique across every account.
        // Enforced here as well as by the pre-insert check, because that check and the insert are not
        // one atomic step: two simultaneous creations of the same name must not both produce a row,
        // and the loser must arrive as a typed conflict rather than as two rows claiming one login.
        builder.HasIndex(sftpUser => sftpUser.FullName)
            .IsUnique()
            .HasDatabaseName("IX_SftpUsers_FullName");

        // Every tenant-scoped read is "this account's logins", which is the query the global filter
        // emits on every single request from a customer.
        builder.HasIndex(sftpUser => sftpUser.AccountId).HasDatabaseName("IX_SftpUsers_AccountId");
    }
}
