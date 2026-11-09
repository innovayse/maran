using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MailAutoresponder"/> onto the <c>mail.MailAutoresponders</c> table.</summary>
public sealed class MailAutoresponderConfiguration : IEntityTypeConfiguration<MailAutoresponder>
{
    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="MailAutoresponder"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MailAutoresponder> builder)
    {
        builder.ToTable("MailAutoresponders");
        builder.HasKey(responder => responder.Id);
        builder.Property(responder => responder.Id).ValueGeneratedNever();

        builder.Property(responder => responder.MailboxId).IsRequired();
        builder.Property(responder => responder.Subject).IsRequired().HasMaxLength(MailDefaults.AutoresponderSubjectMaxLength);
        builder.Property(responder => responder.Body).IsRequired();

        // At most one reply per mailbox.
        builder.HasIndex(responder => responder.MailboxId).IsUnique().HasDatabaseName("IX_MailAutoresponders_MailboxId");

        builder.HasOne(responder => responder.Mailbox)
            .WithMany()
            .HasForeignKey(responder => responder.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
