using Maran.Modules.Sites.Domain.Entities;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Visits every site an account lifecycle step lists, one at a time under that site's lock, and fails the
/// step once all of them were tried if any was refused.
/// </summary>
/// <remarks>
/// The account suspension, resumption and deletion cascades share one shape: one site at a time and never
/// two at once, each waited for within the cascade's total budget (<see cref="SiteLockBudget"/>, which the
/// request an administrator is watching cannot outlast), every site tried before the step fails so one
/// refusal does not leave the others as they were, and a failure the retry of the whole command repairs
/// because every step is idempotent. What differs is the step; the loop is written here once.
/// </remarks>
internal static class SiteCascade
{
    /// <summary>Runs <paramref name="step"/> on every listed site, each under its own lock.</summary>
    /// <param name="listed">The sites the pass listed; only their ids and domains are used here.</param>
    /// <param name="locks">The shared per-site lock.</param>
    /// <param name="clock">The panel's clock, which the waiting budget runs on.</param>
    /// <param name="accountId">The account the cascade is for, named in the failure.</param>
    /// <param name="outcome">What the step does to a vhost, in the past tense, completing "could not be ...".</param>
    /// <param name="step">
    /// The work on one site, run once its lock is held and given what is left of the waiting budget for any
    /// further lock it needs. It reads the row again: the listing may be stale.
    /// </param>
    /// <param name="cancellationToken">Cancels the waits and the steps.</param>
    /// <returns>Resolves when every site was tried and none was refused.</returns>
    /// <exception cref="InvalidOperationException">At least one site could not be taken or its step failed; the message names each.</exception>
    public static async Task RunAsync(
        IReadOnlyList<Site> listed,
        ISiteOperationLock locks,
        IClock clock,
        Guid accountId,
        string outcome,
        Func<Guid, SiteLockBudget, Task<Result>> step,
        CancellationToken cancellationToken)
    {
        var refused = new List<string>();
        var budget = new SiteLockBudget(clock);
        foreach (var site in listed)
        {
            var done = await SiteGuard.RunAsync(
                locks,
                site.Id,
                budget.NextWait(),
                () =>
                {
                    return step(site.Id, budget);
                },
                cancellationToken);
            if (!done.IsSuccess)
            {
                refused.Add($"{site.Domain}: {done.Error?.Code}");
            }
        }

        if (refused.Count > 0)
        {
            throw new InvalidOperationException(
                $"the vhosts of account {accountId} could not be {outcome}: {string.Join("; ", refused)}");
        }
    }
}
