using Maran.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Notifications.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AlertRecipient"/> onto the <c>notifications.AlertRecipients</c> table.</summary>
public sealed class AlertRecipientConfiguration : IEntityTypeConfiguration<AlertRecipient>
{
    /// <summary>The longest a mail address may be.</summary>
    private const int AddressMaxLength = 320;

    /// <summary>Configures the table, key, columns and the unique index for <see cref="AlertRecipient"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AlertRecipient> builder)
    {
        builder.ToTable("AlertRecipients");
        builder.HasKey(recipient => recipient.Id);

        builder.Property(recipient => recipient.Id)
            .ValueGeneratedNever();

        builder.Property(recipient => recipient.Address)
            .IsRequired()
            .HasMaxLength(AddressMaxLength);

        builder.Property(recipient => recipient.Position)
            .IsRequired();

        builder.Property(recipient => recipient.AddedAt)
            .IsRequired();

        // Addresses are stored lower-cased, so this index makes two spellings of one mailbox one row.
        builder.HasIndex(recipient => recipient.Address)
            .IsUnique()
            .HasDatabaseName("IX_AlertRecipients_Address");
    }
}
