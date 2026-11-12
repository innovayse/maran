using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Queries.CountExpiringLicence;

/// <summary>
/// Handles <see cref="CountExpiringLicenceQuery"/> by asking the licence window, the memory the request-time gate
/// reads, whether the installed licence is within a warning threshold.
/// </summary>
/// <remarks>
/// <b>Never <c>Result{T}.Fail</c>.</b> The window never throws and an absent, refused or expired licence is a
/// zero, not a failure; a dashboard that cannot read the licence at all reports the section unavailable on its own.
/// Reading through the cache is deliberate: the dashboard polls, and the answer may be as old as the window's own
/// memory (<see cref="LicenceCoverageCache.TimeToLive"/>), the same age the gate works with.
/// </remarks>
public sealed class CountExpiringLicenceQueryHandler
{
    /// <summary>The licence window's shared memory.</summary>
    private readonly LicenceCoverageCache _cache;

    /// <summary>Says whether the licence is close to its expiry.</summary>
    private readonly LicenceExpiryWarningSource _expiryWarnings;

    /// <summary>Creates the handler.</summary>
    /// <param name="cache">The licence window's shared memory.</param>
    /// <param name="expiryWarnings">Says whether the licence is close to its expiry.</param>
    public CountExpiringLicenceQueryHandler(LicenceCoverageCache cache, LicenceExpiryWarningSource expiryWarnings)
    {
        _cache = cache;
        _expiryWarnings = expiryWarnings;
    }

    /// <summary>Counts the licences within a warning threshold.</summary>
    /// <param name="query">The request; it carries nothing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>1 when the installed licence is close to its expiry, otherwise 0.</returns>
    public async Task<Result<int>> HandleAsync(CountExpiringLicenceQuery query, CancellationToken cancellationToken)
    {
        var coverage = await _cache.GetAsync(cancellationToken);

        return Result<int>.Ok(_expiryWarnings.For(coverage.Status) is null ? 0 : 1);
    }
}
