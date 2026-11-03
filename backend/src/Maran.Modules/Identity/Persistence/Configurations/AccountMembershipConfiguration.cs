using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AccountMembership"/> onto the <c>identity.AccountMemberships</c>
/// table.
/// </summary>
public sealed class AccountMembershipConfiguration : IEntityTypeConfiguration<AccountMembership>
{
    /// <summary>Longest stored role name; the enum is stored as its name.</summary>
    private const int RoleLength = 16;

    /// <summary>Longest stored state name; the enum is stored as its name.</summary>
    private const int StateLength = 16;

    /// <summary>Configures the table, keys, and column constraints for <see cref="AccountMembership"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AccountMembership> builder)
    {
        builder.ToTable("AccountMemberships");
        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.UserId).IsRequired();
        builder.Property(membership => membership.AccountId).IsRequired();

        builder.Property(membership => membership.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(RoleLength);

        // Required with a database default so the additive migration can add the column to a table
        // that already has rows: every existing membership is active until the migration's backfill
        // says otherwise.
        builder.Property(membership => membership.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(StateLength)
            .HasDefaultValue(MembershipState.Active);

        // Stored as the flag set's number (bigint), the same way a plan stores its features.
        builder.Property(membership => membership.Grants).IsRequired();
        builder.Property(membership => membership.CreatedAt).IsRequired();

        // A login holds at most one membership per account: a second would be two answers to "in
        // what capacity", and the database refuses it so a racing redelivery cannot create one.
        builder.HasIndex(membership => new { membership.UserId, membership.AccountId })
            .IsUnique()
            .HasDatabaseName("UX_AccountMemberships_UserId_AccountId");

        // At most one OWNER per account: two would be two keys to the same account, and the database
        // refuses the second so a concurrent redelivery of AccountCreated or a double-clicked resend
        // cannot create it. This is what the dropped UX_Users_AccountId used to guarantee from the
        // login's side. Partial, because any number of members may be added to one account. It is
        // keyed on (AccountId, Role) rather than on AccountId alone because EF Core refuses a second
        // index over the same property set as IX_AccountMemberships_AccountId below.
        builder.HasIndex(membership => new { membership.AccountId, membership.Role })
            .IsUnique()
            .HasFilter("\"Role\" = 'Owner'")
            .HasDatabaseName("UX_AccountMemberships_AccountId_Owner");

        // Account deletion removes every membership of the account, and the residue audit counts
        // them by it: an index scan, not a table scan.
        builder.HasIndex(membership => membership.AccountId)
            .HasDatabaseName("IX_AccountMemberships_AccountId");

        // A membership is a permission to act, so it must not outlive the login it belongs to.
        // There is deliberately no foreign key to the account: the accounts live in another
        // module's schema, which this module never reads (rules/architecture.md), and
        // AccountDeletingHandler is what removes a deleted account's memberships.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .HasConstraintName("FK_AccountMemberships_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
