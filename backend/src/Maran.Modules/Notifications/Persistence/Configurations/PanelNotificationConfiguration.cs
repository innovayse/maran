using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Maran.Modules.Notifications.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="PanelNotification"/> onto the <c>notifications.PanelNotifications</c> table.</summary>
public sealed class PanelNotificationConfiguration : IEntityTypeConfiguration<PanelNotification>
{
    /// <summary>Upper bound on the stored name of a <see cref="NotificationSource"/> or <see cref="NotificationSeverity"/> member.</summary>
    private const int EnumNameMaxLength = 32;

    /// <summary>Configures the table, key, columns and index for <see cref="PanelNotification"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PanelNotification> builder)
    {
        builder.ToTable("PanelNotifications");
        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id)
            .ValueGeneratedNever();

        // Stored by name, so a reader of the table sees "Task" and not 1, and a value added to the
        // Sdk's enum later cannot silently shift the meaning of a stored number.
        builder.Property(notification => notification.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(notification => notification.Code)
            .IsRequired()
            .HasMaxLength(PanelNotification.CodeMaxLength);

        builder.Property(notification => notification.Severity)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(notification => notification.Subject)
            .IsRequired()
            .HasMaxLength(PanelNotification.SubjectMaxLength);

        // One column holding up to four short values joined by the unit separator; the value object
        // is the only thing that reads or writes that format.
        builder.Property(notification => notification.Arguments)
            .IsRequired()
            .HasMaxLength(PanelNotificationArguments.StoredMaxLength)
            .HasConversion(new ValueConverter<PanelNotificationArguments, string>(
                arguments => arguments.Serialize(),
                stored => PanelNotificationArguments.Parse(stored)));

        builder.Property(notification => notification.DeepLink)
            .HasMaxLength(PanelNotification.DeepLinkMaxLength);

        builder.Property(notification => notification.OccurredAt)
            .IsRequired();

        // The feed is newest first, and retention deletes by age: both read this column.
        builder.HasIndex(notification => notification.OccurredAt)
            .HasDatabaseName("IX_PanelNotifications_OccurredAt");
    }
}
