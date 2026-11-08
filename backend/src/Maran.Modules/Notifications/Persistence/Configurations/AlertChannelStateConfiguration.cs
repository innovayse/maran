using Maran.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Notifications.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AlertChannelState"/> onto the <c>notifications.AlertChannelStates</c> table.</summary>
public sealed class AlertChannelStateConfiguration : IEntityTypeConfiguration<AlertChannelState>
{
    /// <summary>Configures the table, key and columns for <see cref="AlertChannelState"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AlertChannelState> builder)
    {
        builder.ToTable("AlertChannelStates");

        // The channel is the key: one row per channel, so a second row cannot exist.
        builder.HasKey(state => state.Channel);

        builder.Property(state => state.Channel)
            .ValueGeneratedNever();

        builder.Property(state => state.LastAttemptAt)
            .IsRequired();

        builder.Property(state => state.LastSucceededAt);

        builder.Property(state => state.LastFailedAt);

        builder.Property(state => state.LastFailureCode)
            .HasMaxLength(AlertChannelState.FailureCodeMaxLength);

        // Computed from the two timestamps, never stored.
        builder.Ignore(state => state.IsFailing);
    }
}
