using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Persistence;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// Writes the outcome of an operator alert into its channel's <see cref="AlertChannelState"/>, so the
/// dashboard can say whether alerts are arriving.
/// </summary>
/// <remarks>
/// Creates the channel's row on its first outcome. The caller (a background handler that must never
/// throw) wraps the call; this type does not swallow, because a recorder that hid its own failure
/// would be the silent path the state exists to remove.
/// </remarks>
public sealed class AlertChannelStateRecorder
{
    /// <summary>The module's database context, which owns the channel states.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the recorder.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock, which stamps the outcome.</param>
    public AlertChannelStateRecorder(NotificationsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Records that an alert arrived on a channel.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task RecordSuccessAsync(AlertChannel channel, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var state = await LoadOrAddAsync(channel, now, cancellationToken);
        state.RecordSuccess(now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records that an alert did not arrive on a channel.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="failureCode">The error code of the failure; never the server's own text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task RecordFailureAsync(AlertChannel channel, string failureCode, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var state = await LoadOrAddAsync(channel, now, cancellationToken);
        state.RecordFailure(now, failureCode);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Finds the channel's row or adds a new one to the context.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="now">The time to stamp a new row with.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tracked row.</returns>
    private async Task<AlertChannelState> LoadOrAddAsync(
        AlertChannel channel,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = await _dbContext.AlertChannelStates
            .FirstOrDefaultAsync(row => row.Channel == channel, cancellationToken);

        if (state is null)
        {
            state = new AlertChannelState(channel, now);
            _dbContext.AlertChannelStates.Add(state);
        }

        return state;
    }
}
