using Maran.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Notifications.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="NotificationDismissal"/> onto the <c>notifications.NotificationDismissals</c> table.</summary>
public sealed class NotificationDismissalConfiguration : IEntityTypeConfiguration<NotificationDismissal>
{
    /// <summary>Configures the table, composite key and the cascade from the notification.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<NotificationDismissal> builder)
    {
        builder.ToTable("NotificationDismissals");

        // The pair is the identity: one administrator dismisses one notification at most once.
        builder.HasKey(dismissal => new { dismissal.NotificationId, dismissal.UserId });

        builder.Property(dismissal => dismissal.DismissedAt)
            .IsRequired();

        // Cascade, so retention deleting a notification takes its dismissals with it and no
        // per-administrator residue outlives the entry it was about.
        builder.HasOne<PanelNotification>()
            .WithMany()
            .HasForeignKey(dismissal => dismissal.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The feed asks "which of these has THIS administrator dismissed": the key leads with the
        // notification, so the by-administrator read needs its own index.
        builder.HasIndex(dismissal => dismissal.UserId)
            .HasDatabaseName("IX_NotificationDismissals_UserId");
    }
}
