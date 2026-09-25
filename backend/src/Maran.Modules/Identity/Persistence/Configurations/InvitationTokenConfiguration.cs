using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="InvitationToken"/> onto the
/// <c>identity.InvitationTokens</c> table.
/// </summary>
public sealed class InvitationTokenConfiguration : IEntityTypeConfiguration<InvitationToken>
{
    /// <summary>Length of a base64-encoded SHA-256 digest, including its padding.</summary>
    private const int TokenHashLength = 44;

    /// <summary>Configures the table, key, and column constraints for <see cref="InvitationToken"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<InvitationToken> builder)
    {
        builder.ToTable("InvitationTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.UserId).IsRequired();

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(TokenHashLength);

        builder.Property(token => token.CreatedAt).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();

        // Unique, and that is a correctness constraint rather than tidiness: the acceptance handler
        // finds a token by its digest with a single-row read, and two rows carrying one digest would
        // make that read ambiguous — which of the two got consumed would decide whether a replay
        // worked.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_InvitationTokens_TokenHash");

        // The invited user has exactly one outstanding invitation at a time, and re-inviting them
        // retires the old row before issuing a new one — a lookup by user, not by digest. Without
        // the index that is a full scan on an anonymous, public endpoint.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("IX_InvitationTokens_UserId");

        // An outstanding invitation is a live permission to set a login's first password, so it must
        // not be able to outlive the login it names — the same rule PasswordResetTokens follows.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .HasConstraintName("FK_InvitationTokens_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
