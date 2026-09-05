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
            .HasMaxLength(64);

        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.Subject)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.IpAddress)
            .IsRequired()
            .HasMaxLength(45);

        builder.Property(e => e.UserAgent)
            .IsRequired()
            .HasMaxLength(UserAgentText.MaxLength);

        builder.Property(e => e.Succeeded)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(64);

        // The audit screen reads newest-first, and an operator asking "what did this user do"
        // filters by actor; both get an index so the journal stays readable as it grows.
        builder.HasIndex(e => e.OccurredAt).IsDescending().HasDatabaseName("IX_AuditEvents_OccurredAt");
        builder.HasIndex(e => e.ActorUserId).HasDatabaseName("IX_AuditEvents_ActorUserId");

        // Deliberately NO foreign key to Users: the journal outlives the accounts it describes.
        // A deleted user must not take the record of what they did with them, and a failed login
        // names a user who never existed at all.
    }
}
