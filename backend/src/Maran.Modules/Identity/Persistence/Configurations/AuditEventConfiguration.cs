using Maran.Modules.Identity.Domain.Entities;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AuditEvent"/> onto the <c>identity.AuditEvents</c> table.</summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="AuditEvent"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OccurredAt)
            .IsRequired();

        builder.Property(e => e.ActorUsername)
            .IsRequired()
            .HasMaxLength(AuditEvent.ActorUsernameMaxLength);

        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(AuditEvent.ActionMaxLength);

        builder.Property(e => e.Subject)
            .IsRequired()
            .HasMaxLength(AuditEvent.SubjectMaxLength);

        builder.Property(e => e.IpAddress)
            .IsRequired()
            .HasMaxLength(AuditEvent.IpAddressMaxLength);

        builder.Property(e => e.UserAgent)
            .IsRequired()
            .HasMaxLength(UserAgentText.MaxLength);

        builder.Property(e => e.Succeeded)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(AuditEvent.CorrelationIdMaxLength);

        // Who was viewing as the actor (issue #317), one optional value over two columns that are
        // both null when the actor acted as themselves, so a name can never sit beside a missing
        // id. The name has the same width as the actor's own name column.
        //
        // Migration note: the columns come from the UNRELEASED migration
        // 20261102104553_AddMembershipsPinsAndViewAs and keep their names and types, so the
        // migration agent only regenerates the designer and the snapshot. No backfill SQL is
        // needed: no shipped row has either column, and a development row that has the id also has
        // the name (the old constructor nulled the name only when the id was null).
        builder.OwnsOne(e => e.Impersonator, impersonator =>
        {
            impersonator.Property(i => i.OperatorId)
                .HasColumnName("ImpersonatorUserId")
                .IsRequired();

            impersonator.Property(i => i.Username)
                .HasColumnName("ImpersonatorUsername")
                .HasMaxLength(AuditEvent.ActorUsernameMaxLength)
                .IsRequired();
        });

        // The columns the hash chain, the account filter and erasure added. All nullable, so the
        // migration is expand-only and the previous release, which never reads or writes them, runs
        // unchanged against the new schema. The two hashes and the salt are fixed-length byte values
        // (AuditEvent.HashLength, AuditEvent.SaltLength); bytea cannot state a length, so the entity's
        // Seal is what holds it.
        builder.Property(e => e.AccountId);

        // The login a row is ABOUT (an owner changing a sub-user's grants). Nullable, so this too is an
        // expand-only change, and indexed because erasing a login looks its rows up by this column.
        builder.Property(e => e.SubjectUserId);
        builder.Property(e => e.ChainIndex);
        builder.Property(e => e.RowHash).HasColumnType("bytea");
        builder.Property(e => e.ErasableCommitment).HasColumnType("bytea");
        builder.Property(e => e.CommitmentSalt).HasColumnType("bytea");

        // A concurrency token, so the sealer cannot overwrite an erasure that committed between its
        // read and its save: sealing an unerased row stores a salt, and a save that went through after
        // the erasure would put back the very salt the erasure destroyed. The save is refused instead
        // (DbUpdateConcurrencyException) and the row is sealed on the next maintenance tick. No schema
        // effect: the token only adds the column to the WHERE of an UPDATE.
        builder.Property(e => e.ErasedAt).IsConcurrencyToken();

        // Computed from the columns above; never stored.
        builder.Ignore(e => e.IsSealed);
        builder.Ignore(e => e.IsErased);

        // The audit screen reads newest-first, and an operator asking "what did this user do"
        // filters by actor; both get an index so the journal stays readable as it grows.
        builder.HasIndex(e => e.OccurredAt).IsDescending().HasDatabaseName("IX_AuditEvents_OccurredAt");
        builder.HasIndex(e => e.ActorUserId).HasDatabaseName("IX_AuditEvents_ActorUserId");
        builder.HasIndex(e => e.SubjectUserId).HasDatabaseName("IX_AuditEvents_SubjectUserId");

        // The journal screen filters by one action and its action filter lists the distinct actions
        // the journal holds (ListAuditActionChoicesQuery). The table only grows, so both would be a
        // sequential pass over every entry ever written without this index.
        builder.HasIndex(e => e.Action).HasDatabaseName("IX_AuditEvents_Action");

        // The account filter, and the account export and erasure that follow the same column.
        builder.HasIndex(e => e.AccountId).HasDatabaseName("IX_AuditEvents_AccountId");

        // Two FILTERED indexes, because most rows are in one state or the other at a given time.
        // The unique one is what makes sealing a row twice fail instead of forking the chain, and
        // it ignores the unsealed rows, which all have a null index. The other lets the sealer find
        // the rows still waiting for a place without a pass over the sealed bulk of the table.
        builder.HasIndex(e => e.ChainIndex)
            .IsUnique()
            .HasFilter("\"ChainIndex\" IS NOT NULL")
            .HasDatabaseName("IX_AuditEvents_ChainIndex");

        // NAMED in the model (the second argument), because OccurredAt already carries the descending
        // index above: an unnamed HasIndex over the same property returns THAT index, and the filter and
        // the new database name would be written onto it, renaming a shipped index instead of adding one.
        builder.HasIndex(e => e.OccurredAt, "IX_AuditEvents_Unsealed")
            .HasFilter("\"ChainIndex\" IS NULL")
            .HasDatabaseName("IX_AuditEvents_Unsealed");

        // Deliberately NO foreign key to Users: the journal outlives the accounts it describes.
        // A deleted user must not take the record of what they did with them, and a failed login
        // names a user who never existed at all.
    }
}
