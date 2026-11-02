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
            impersonator.Property(i => i.UserId)
                .HasColumnName("ImpersonatorUserId")
                .IsRequired();

            impersonator.Property(i => i.Username)
                .HasColumnName("ImpersonatorUsername")
                .HasMaxLength(AuditEvent.ActorUsernameMaxLength)
                .IsRequired();
        });

        // The audit screen reads newest-first, and an operator asking "what did this user do"
        // filters by actor; both get an index so the journal stays readable as it grows.
        builder.HasIndex(e => e.OccurredAt).IsDescending().HasDatabaseName("IX_AuditEvents_OccurredAt");
        builder.HasIndex(e => e.ActorUserId).HasDatabaseName("IX_AuditEvents_ActorUserId");

        // The journal screen filters by one action and its action filter lists the distinct actions
        // the journal holds (ListAuditActionChoicesQuery). The table only grows, so both would be a
        // sequential pass over every entry ever written without this index.
        builder.HasIndex(e => e.Action).HasDatabaseName("IX_AuditEvents_Action");

        // Deliberately NO foreign key to Users: the journal outlives the accounts it describes.
        // A deleted user must not take the record of what they did with them, and a failed login
        // names a user who never existed at all.
    }
}
