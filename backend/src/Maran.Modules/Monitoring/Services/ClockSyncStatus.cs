using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// The Monitoring module's <see cref="IClockSyncStatus"/>: answers from the clock's alert row, which
/// <see cref="AlertObserver"/> updates with every answered reading (issue #150).
/// </summary>
/// <remarks>
/// The row's latest observation is what is read, not whether an episode is open: an alert waits for
/// ten breaching rounds before it mails anybody, while a person whose code was just refused needs
/// to know about the clock now. A round in which the agent could not read the clock leaves the row
/// untouched, so the answer is the latest reading that was actually taken.
/// </remarks>
public sealed class ClockSyncStatus : IClockSyncStatus
{
    /// <summary>The module's database context, which owns the alert rows.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>Creates the window.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ClockSyncStatus(MonitoringDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> IsClockUnsynchronizedAsync(CancellationToken cancellationToken)
    {
        var state = await _dbContext.AlertStates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Kind == AlertKind.ClockUnsynchronized && row.Subject == AlertEvaluator.SystemClockSubject,
                cancellationToken);

        return state is not null && state.LastObservationBreached();
    }
}
