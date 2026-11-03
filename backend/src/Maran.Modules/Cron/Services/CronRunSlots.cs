using Maran.Modules.Cron.Models;
using Maran.Modules.Cron.Options;
using Maran.Modules.Cron.Resources;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Cron.Services;

/// <summary>
/// Counts the runs started by hand that are in flight in this panel process, per account and in
/// total, and refuses a new one past the bounds (issue #333).
/// </summary>
/// <remarks>
/// <para>
/// A singleton, because the count has to be shared by every request; it holds only two integers per
/// account and one total, and nothing here is persisted — a restart of the panel also ends every
/// call it was waiting on, so starting from zero is the true count.
/// </para>
/// <para>
/// The two bounds refuse with different codes because the advice differs. An account at its own
/// bound is told a run of its own is still going (<c>Conflict</c>: wait for it); an account refused
/// by the panel-wide bound did nothing wrong and is told the server is busy (<c>Unavailable</c>:
/// try again). The account bound is checked first so a customer over their own limit never learns
/// anything about how busy the server is.
/// </para>
/// </remarks>
public sealed class CronRunSlots
{
    /// <summary>The bounds, read at each acquisition so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<CronRunOptions> _options;

    /// <summary>Guards both counters, which must change together.</summary>
    private readonly object _gate = new();

    /// <summary>The runs in flight per account; an account with none has no entry.</summary>
    private readonly Dictionary<Guid, int> _perAccount = [];

    /// <summary>The runs in flight across every account.</summary>
    private int _total;

    /// <summary>Creates the counter.</summary>
    /// <param name="options">The per-account and panel-wide bounds.</param>
    public CronRunSlots(IOptionsMonitor<CronRunOptions> options)
    {
        _options = options;
    }

    /// <summary>Takes a slot for a run of <paramref name="accountId"/>, or refuses.</summary>
    /// <param name="accountId">The account whose entry is about to run.</param>
    /// <returns>
    /// A lease to dispose when the run is over — or <c>CronRunAccountLimitReached</c>, or
    /// <c>CronRunServerBusy</c>.
    /// </returns>
    public Result<CronRunLease> TryAcquire(Guid accountId)
    {
        var bounds = _options.CurrentValue;

        lock (_gate)
        {
            _perAccount.TryGetValue(accountId, out var held);
            if (held >= bounds.MaxConcurrentPerAccount)
            {
                return Result<CronRunLease>.Fail(
                    Error.Of(nameof(ErrorMessages.CronRunAccountLimitReached), ErrorType.Conflict));
            }

            if (_total >= bounds.MaxConcurrentGlobal)
            {
                return Result<CronRunLease>.Fail(
                    Error.Of(nameof(ErrorMessages.CronRunServerBusy), ErrorType.Unavailable));
            }

            _perAccount[accountId] = held + 1;
            _total++;
        }

        return Result<CronRunLease>.Ok(new CronRunLease(() =>
        {
            Release(accountId);
        }));
    }

    /// <summary>Gives back one slot taken for <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account the slot was taken for.</param>
    private void Release(Guid accountId)
    {
        lock (_gate)
        {
            _total--;
            if (_perAccount[accountId] <= 1)
            {
                _perAccount.Remove(accountId);
            }
            else
            {
                _perAccount[accountId]--;
            }
        }
    }
}
