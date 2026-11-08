using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Mailbox"/> onto the <c>mail.Mailboxes</c> table.</summary>
/// <remarks>
/// Read the column list for what is NOT here: there is no plaintext password and no encrypted one, only
/// <see cref="Mailbox.PasswordHash"/>. A test asserts that by reflection.
/// </remarks>
public sealed class MailboxConfiguration : IEntityTypeConfiguration<Mailbox>
{
    /// <summary>The longest stored hash, matching the agent's ceiling for a password hash.</summary>
    private const int PasswordHashMaxLength = 255;

    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="Mailbox"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Mailbox> builder)
    {
        builder.ToTable("Mailboxes");
        builder.HasKey(mailbox => mailbox.Id);
        builder.Property(mailbox => mailbox.Id).ValueGeneratedNever();

        builder.Property(mailbox => mailbox.AccountId).IsRequired();
        builder.Property(mailbox => mailbox.MailDomainId).IsRequired();
        builder.Property(mailbox => mailbox.LocalPart).IsRequired().HasMaxLength(MailDefaults.LocalPartMaxLength);
        builder.Property(mailbox => mailbox.PasswordHash).IsRequired().HasMaxLength(PasswordHashMaxLength);
        builder.Property(mailbox => mailbox.QuotaMib).IsRequired();
        builder.Property(mailbox => mailbox.SendLimitPerHour).IsRequired();
        builder.Property(mailbox => mailbox.Enabled).IsRequired();
        builder.Property(mailbox => mailbox.SpamAction).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(mailbox => mailbox.CreatedAt).IsRequired();

        // One address, one mailbox. The handler checks first (a mailbox may not share an address with an
        // alias, which no index across two tables can state); this index closes the race between two
        // simultaneous creations of the same address.
        builder.HasIndex(mailbox => new { mailbox.MailDomainId, mailbox.LocalPart })
            .IsUnique()
            .HasDatabaseName("IX_Mailboxes_MailDomainId_LocalPart");

        // The query the tenant filter emits on every customer request, and the plan-limit count.
        builder.HasIndex(mailbox => mailbox.AccountId).HasDatabaseName("IX_Mailboxes_AccountId");

        builder.HasOne(mailbox => mailbox.MailDomain)
            .WithMany()
            .HasForeignKey(mailbox => mailbox.MailDomainId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
