using Maran.Modules.Identity.Domain.Entities;
using Maran.SharedKernel.Utilities.Network;
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

        // The pair that says how a session ended, stored in the two columns the first release
        // created ("RevokedAt", "RevocationReason") and read as one optional value: both columns are
        // null while the session is live. No schema change; the owned type only reads the old columns
        // together. Presence is decided by the instant, the one column that is required inside the
        // value.
        builder.OwnsOne(s => s.Revocation, revocation =>
        {
            revocation.Property(r => r.At)
                .HasColumnName("RevokedAt")
                .IsRequired();

            revocation.Property(r => r.Reason)
                .HasColumnName("RevocationReason")
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
        });

        builder.Property(s => s.IpAddress)
            .IsRequired()
            .HasMaxLength(45);

        builder.Property(s => s.UserAgent)
            .IsRequired()
            .HasMaxLength(UserAgentText.MaxLength);

        // Who is behind a view-as session: the operator and the operator's own session. Both columns
        // are null for an own sign-in, so there is no kind column to disagree with them: a row is a
        // view-as session exactly when the origin is present. Presence is decided by the required
        // properties of the owned value, so a half-filled pair can never read as an origin.
        //
        // Migration note (20261102104553_AddMembershipsPinsAndViewAs is UNRELEASED, on no remote
        // branch, so it is edited in place rather than followed by a new migration): drop its
        // AddColumn "Kind" from Up and Down; "ActorUserId" and "ActorSessionId" stay as they are.
        // Backfill SQL for a database that already applied the earlier shape of that migration
        // (development databases only; nothing shipped ever held a Kind column):
        //   -- raw-sql: forget the retired discriminator; a view-as row already has both actor ids.
        //   ALTER TABLE identity."Sessions" DROP COLUMN IF EXISTS "Kind";
        // No other data step is needed: "RevokedAt"/"RevocationReason" are unchanged columns and
        // the actor columns keep their names and values.
        builder.OwnsOne(s => s.ViewAs, origin =>
        {
            origin.Property(o => o.ActorUserId)
                .HasColumnName("ActorUserId")
                .IsRequired();

            origin.Property(o => o.ActorSessionId)
                .HasColumnName("ActorSessionId")
                .IsRequired();
        });


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
