
namespace Maran.Modules.Sites.Services;

/// <summary>
/// The time one account cascade (suspend, resume, delete) may spend waiting for the locks of all its sites
/// together, so a request an administrator is watching does not last N waits.
/// </summary>
/// <remarks>
/// The cascade runs inside the administrator's HTTP request. With a fixed wait per site an account whose
/// sites are parked behind slow operations would hold the request for the wait times the number of sites,
/// past any proxy timeout. Each site is given the shorter of <see cref="SiteLockWait.Cascade"/> and what is
/// left of <see cref="SiteLockWait.CascadeTotal"/>; once nothing is left a site is still asked for, without
/// waiting, so a free site is always done and only a site that really is busy is refused. A refused site
/// fails the cascade after every site was tried; the account is left as it was, and repeating the command is
/// the retry, because every step of the cascade is idempotent.
/// </remarks>
public sealed class SiteLockBudget
{
    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>When the budget began.</summary>
    private readonly DateTimeOffset _started;

    /// <summary>Starts the budget now.</summary>
    /// <param name="clock">The panel's clock, so a test can move time.</param>
    public SiteLockBudget(IClock clock)
    {
        _clock = clock;
        _started = clock.UtcNow;
    }

    /// <summary>How long the next site may be waited for.</summary>
    /// <returns>At most <see cref="SiteLockWait.Cascade"/>; <see cref="TimeSpan.Zero"/> once the budget is spent.</returns>
    public TimeSpan NextWait()
    {
        var left = SiteLockWait.CascadeTotal - (_clock.UtcNow - _started);
        if (left <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return left < SiteLockWait.Cascade ? left : SiteLockWait.Cascade;
    }
}
