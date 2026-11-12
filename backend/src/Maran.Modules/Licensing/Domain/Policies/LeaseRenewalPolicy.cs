using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The ONE rule of when the panel asks the licence service for a lease, how long it waits before asking again,
/// what counts as a failed pass and when repeated failure becomes an alert. Pure over its arguments (the clock
/// and the randomness arrive as values), so every transition has a unit test and the worker that runs the
/// passes holds no judgement of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Renewal is daily and the lease lives fourteen days.</b> A pass renews only a lease older than
/// <see cref="MinimumLeaseAge"/>, so a panel that restarts often does not ask the service on every start, and
/// the success interval sits just under <see cref="LeaseGracePolicy.RenewalInterval"/> so a lease is replaced
/// before it is reported as overdue.
/// </para>
/// <para>
/// <b>Jitter.</b> Every wait is a fixed base plus a share of a span, chosen by the caller from a random
/// fraction in [0, 1). Panels installed from one image would otherwise all ask at the same minute every day.
/// </para>
/// <para>
/// <b>Failure is an alert only when it repeats.</b> One unreachable pass is the ordinary state of a server with
/// a flaky uplink and the old lease still covers it for up to fourteen days; <see cref="AlertThreshold"/>
/// consecutive failed passes (the daily renewal and two retries) are a condition an operator should hear about
/// while the lease still has most of its life left.
/// </para>
/// </remarks>
public static class LeaseRenewalPolicy
{
    /// <summary>How many consecutive failed passes raise the alert.</summary>
    public const int AlertThreshold = 3;

    /// <summary>A lease issued more recently than this is not renewed.</summary>
    public static readonly TimeSpan MinimumLeaseAge = TimeSpan.FromHours(20);

    /// <summary>The wait after the first pass of the process, before any jitter.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    /// <summary>The span the start-up wait may be lengthened by.</summary>
    public static readonly TimeSpan StartupJitter = TimeSpan.FromMinutes(8);

    /// <summary>The wait after a lease was stored, before any jitter.</summary>
    private static readonly TimeSpan RenewedWait = TimeSpan.FromHours(22);

    /// <summary>The span the wait after a stored lease may be lengthened by.</summary>
    private static readonly TimeSpan RenewedJitter = TimeSpan.FromHours(2);

    /// <summary>The wait after a failed pass, before any jitter.</summary>
    private static readonly TimeSpan FailedWait = TimeSpan.FromHours(6);

    /// <summary>The span the wait after a failed pass may be lengthened by.</summary>
    private static readonly TimeSpan FailedJitter = TimeSpan.FromHours(1);

    /// <summary>The wait after a pass that found the lease recent enough, before any jitter.</summary>
    private static readonly TimeSpan NotDueWait = TimeSpan.FromHours(2);

    /// <summary>The span the wait after a not-due pass may be lengthened by.</summary>
    private static readonly TimeSpan NotDueJitter = TimeSpan.FromMinutes(30);

    /// <summary>The wait after a pass that had no licence to renew for, before any jitter.</summary>
    private static readonly TimeSpan IdleWait = TimeSpan.FromHours(1);

    /// <summary>The span the wait after an idle pass may be lengthened by.</summary>
    private static readonly TimeSpan IdleJitter = TimeSpan.FromMinutes(10);

    /// <summary>Whether the stored lease is old enough, absent or unusable for this licence, so the service is to be asked.</summary>
    /// <param name="stored">What the lease file amounted to.</param>
    /// <param name="licence">The installed, verified licence.</param>
    /// <param name="now">The panel's clock.</param>
    /// <returns>True when a renewal is due.</returns>
    public static bool IsDue(LeaseReading stored, Licence licence, DateTimeOffset now)
    {
        if (stored is not LeaseReading.Present present)
        {
            return true;
        }

        var lease = present.Lease;
        if (!string.Equals(lease.LicenceId, licence.Id.Value, StringComparison.Ordinal))
        {
            return true;
        }

        if (lease.IssuedAt > now + LeaseGracePolicy.SkewBound)
        {
            return true;
        }

        return now - lease.IssuedAt >= MinimumLeaseAge || now >= lease.ValidUntil;
    }

    /// <summary>
    /// Whether a genuine lease would replace the stored one rather than shorten or repeat it. A stored lease
    /// that does not stand as good (absent, for another licence, over, future-dated) is always replaced.
    /// </summary>
    /// <param name="stored">How the stored lease stands now.</param>
    /// <param name="candidate">The lease the service issued.</param>
    /// <returns>True when writing the candidate loses nothing the stored lease still gives.</returns>
    public static bool Improves(LeaseStanding stored, LicenceLease candidate)
    {
        if (stored.Kind is not (LeaseStandingKind.Fresh or LeaseStandingKind.RenewalOverdue)
            || stored.ValidUntil is null)
        {
            return true;
        }

        return candidate.ValidUntil > stored.ValidUntil;
    }

    /// <summary>Whether a pass counts toward a run of failures.</summary>
    /// <param name="outcome">What the pass amounted to.</param>
    /// <returns>
    /// True for every outcome in which a renewal was due, the service was asked or the answer was in hand, and no
    /// lease was stored. <see cref="LeaseRenewalOutcome.NotConfigured"/> is not one: no request was made, the
    /// operator (or the public demonstration) chose that state, and it is logged on every pass instead.
    /// </returns>
    public static bool IsFailure(LeaseRenewalOutcome outcome)
    {
        return outcome is LeaseRenewalOutcome.Unreachable
            or LeaseRenewalOutcome.Refused
            or LeaseRenewalOutcome.AnswerRejected
            or LeaseRenewalOutcome.NotNewer
            or LeaseRenewalOutcome.WriteFailed;
    }

    /// <summary>How long to wait before the next pass.</summary>
    /// <param name="outcome">What the pass amounted to.</param>
    /// <param name="jitter">A random fraction, at least 0 and below 1, chosen by the caller.</param>
    /// <returns>The wait: a base for the outcome plus <paramref name="jitter"/> of that outcome's span.</returns>
    public static TimeSpan DelayAfter(LeaseRenewalOutcome outcome, double jitter)
    {
        var share = Math.Clamp(jitter, 0.0, 1.0);

        var (wait, span) = outcome switch
        {
            LeaseRenewalOutcome.Renewed => (RenewedWait, RenewedJitter),
            LeaseRenewalOutcome.NotDue => (NotDueWait, NotDueJitter),
            LeaseRenewalOutcome.SkippedOffline
                or LeaseRenewalOutcome.SkippedNoLicence
                or LeaseRenewalOutcome.NotConfigured
                or LeaseRenewalOutcome.LicenceChanged
                or LeaseRenewalOutcome.RenewalInProgress => (IdleWait, IdleJitter),
            _ => (FailedWait, FailedJitter),
        };

        return wait + (span * share);
    }

    /// <summary>The wait before the first pass of a process.</summary>
    /// <param name="jitter">A random fraction, at least 0 and below 1, chosen by the caller.</param>
    /// <returns>The start-up wait plus <paramref name="jitter"/> of its span.</returns>
    public static TimeSpan DelayBeforeFirstPass(double jitter)
    {
        return StartupDelay + (StartupJitter * Math.Clamp(jitter, 0.0, 1.0));
    }

    /// <summary>What the operator is to be told after a pass.</summary>
    /// <param name="outcome">What the pass amounted to.</param>
    /// <param name="consecutiveFailures">The length of the run of failed passes INCLUDING this one; 0 after a success.</param>
    /// <param name="alertRaised">Whether the alert for the current run has already been raised.</param>
    /// <returns>Raise once the run reaches <see cref="AlertThreshold"/>, resolve once after a success, otherwise nothing.</returns>
    public static LeaseRenewalAlertAction AlertActionFor(
        LeaseRenewalOutcome outcome,
        int consecutiveFailures,
        bool alertRaised)
    {
        if (outcome == LeaseRenewalOutcome.Renewed)
        {
            return alertRaised ? LeaseRenewalAlertAction.Resolve : LeaseRenewalAlertAction.None;
        }

        if (IsFailure(outcome) && consecutiveFailures >= AlertThreshold && !alertRaised)
        {
            return LeaseRenewalAlertAction.Raise;
        }

        return LeaseRenewalAlertAction.None;
    }
}
