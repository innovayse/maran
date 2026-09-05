using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PasswordResetToken"/> onto the
/// <c>identity.PasswordResetTokens</c> table.
/// </summary>
public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    /// <summary>Length of a base64-encoded SHA-256 digest, including its padding.</summary>
    private const int TokenHashLength = 44;

    /// <summary>Configures the table, key, and column constraints for <see cref="PasswordResetToken"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.UserId).IsRequired();

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(TokenHashLength);

        builder.Property(token => token.CreatedAt).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();

        // Unique, and that is a correctness constraint rather than tidiness: the reset handler finds
        // a token by its digest with a single-row read, and two rows carrying one digest would make
        // that read ambiguous — which of the two got consumed would decide whether a replay worked.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_PasswordResetTokens_TokenHash");

        // The reset flow reads by digest, but the REQUEST flow retires a user's outstanding tokens
        // before issuing a new one, and that is a lookup by user. Without the index it is a full
        // scan on an anonymous, public endpoint — which is the one place a table scan is also a
        // denial-of-service lever.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("IX_PasswordResetTokens_UserId");
    }
}
