using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;

namespace Maran.Modules.Monitoring.Queries.GetAlertThresholds;

/// <summary>
/// Handles <see cref="GetAlertThresholdsQuery"/>: the stored thresholds, or the defaults on a panel
/// nobody has configured (issue #142).
/// </summary>
/// <remarks>
/// The same fallback the alert observer takes, so the screen shows the thresholds the next round
/// will judge by and never a figure the observer does not use.
/// </remarks>
public sealed class GetAlertThresholdsQueryHandler
{
    /// <summary>The module's database context, which owns the stored thresholds.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>The panel's clock; stamps the default thresholds that are never stored.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public GetAlertThresholdsQueryHandler(MonitoringDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Reads the thresholds.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The thresholds with the bounds a threshold may take.</returns>
    public async Task<Result<AlertThresholdsDto>> HandleAsync(
        GetAlertThresholdsQuery query,
        CancellationToken cancellationToken)
    {
        var thresholds = await _dbContext.AlertThresholds
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == AlertThresholds.SingletonId, cancellationToken)
            ?? AlertThresholds.CreateDefault(_clock.UtcNow);

        return Result<AlertThresholdsDto>.Ok(new AlertThresholdsDto(
            thresholds.DiskPercent,
            thresholds.CpuPercent,
            thresholds.MemoryPercent,
            AlertThresholds.MinimumPercent,
            AlertThresholds.MaximumPercent));
    }
}
