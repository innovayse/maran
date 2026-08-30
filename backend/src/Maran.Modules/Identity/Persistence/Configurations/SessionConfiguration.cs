using Maran.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Session"/> onto the <c>identity.Sessions</c> table.</summary>
public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    /// <summary>Length of a base64-encoded SHA-256 digest, which is what <c>TokenHash</c> holds.</summary>
    private const int TokenHashLength = 44;

    /// <summary>Configures the table, keys, and column constraints for <see cref="Session"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired();

        builder.Property(s => s.FamilyId)
            .IsRequired();

        builder.Property(s => s.TokenHash)
            .IsRequired()
            .HasMaxLength(TokenHashLength);

        builder.Property(s => s.IssuedAt)
            .IsRequired();

        builder.Property(s => s.ExpiresAt)
            .IsRequired();

        builder.Property(s => s.RevocationReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.IpAddress)
            .IsRequired()
            .HasMaxLength(45);

        builder.Property(s => s.UserAgent)
            .IsRequired()
            .HasMaxLength(512);

        // Every refresh looks a session up by this hash, so the index is the hot path; unique
        // because two sessions sharing a token hash would make rotation ambiguous.
        builder.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName("IX_Sessions_TokenHash");

        // Reuse detection revokes a whole family at once, and the sessions screen lists one user's
        // sessions: both are index scans, not table scans.
        builder.HasIndex(s => s.FamilyId).HasDatabaseName("IX_Sessions_FamilyId");
        builder.HasIndex(s => s.UserId).HasDatabaseName("IX_Sessions_UserId");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("FK_Sessions_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
