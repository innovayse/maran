using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AccountMembershipOffer"/> onto the
/// <c>identity.AccountMembershipOffers</c> table.
/// </summary>
public sealed class AccountMembershipOfferConfiguration : IEntityTypeConfiguration<AccountMembershipOffer>
{
    /// <summary>Longest stored outcome name; the enum is stored as its name.</summary>
    private const int OutcomeLength = 16;

    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="AccountMembershipOffer"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AccountMembershipOffer> builder)
    {
        builder.ToTable("AccountMembershipOffers");
        builder.HasKey(offer => offer.Id);

        builder.Property(offer => offer.UserId).IsRequired();
        builder.Property(offer => offer.AccountId).IsRequired();
        builder.Property(offer => offer.AccountName).IsRequired().HasMaxLength(AccountMembershipOffer.AccountNameLength);
        builder.Property(offer => offer.Identifier).IsRequired().HasMaxLength(AccountMembershipOffer.IdentifierLength);

        // Stored as the flag set's number (bigint), the same way a membership stores its grants.
        builder.Property(offer => offer.Grants).IsRequired();
        builder.Property(offer => offer.CreatedAt).IsRequired();
        builder.Property(offer => offer.ExpiresAt).IsRequired();
        builder.Property(offer => offer.ResolvedAt);
        builder.Property(offer => offer.Outcome)
            .HasConversion<string>()
            .HasMaxLength(OutcomeLength);

        // At most one OPEN offer per login and account: a second would mail the person twice for the
        // same thing, and the database refuses it so two racing requests cannot create one. An offer
        // that expired unanswered is still unresolved, so the handler closes it (Expire) before it
        // stores a fresh one; the index alone does not treat expiry as absence.
        builder.HasIndex(offer => new { offer.UserId, offer.AccountId })
            .IsUnique()
            .HasFilter("\"ResolvedAt\" IS NULL")
            .HasDatabaseName("UX_AccountMembershipOffers_UserId_AccountId_Open");

        // The offered login lists its own offers by it.
        builder.HasIndex(offer => offer.UserId)
            .HasDatabaseName("IX_AccountMembershipOffers_UserId");

        // The owner's team list and cap count read by account, and account deletion removes by it.
        builder.HasIndex(offer => offer.AccountId)
            .HasDatabaseName("IX_AccountMembershipOffers_AccountId");

        // An offer must not outlive the login it is addressed to. There is deliberately no foreign
        // key to the account: the accounts live in another module's schema, and
        // AccountDeletingHandler is what removes a deleted account's offers.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(offer => offer.UserId)
            .HasConstraintName("FK_AccountMembershipOffers_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
