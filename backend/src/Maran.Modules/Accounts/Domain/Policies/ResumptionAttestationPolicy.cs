using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// How a reactivation reads what the host answered about an account, and words it for the task
/// journal: the stateless half of <c>ReactivateAccountCommandHandler</c>, kept apart so the handler
/// holds the orchestration and this holds the rules over the host's answer.
/// </summary>
/// <remarks>
/// Moved out of the handler unchanged. The attestation's asymmetry with suspension's, and what
/// COMPLETED may mean, are stated on the handler's <c>HandleAsync</c>.
/// </remarks>
public static class ResumptionAttestationPolicy
{
    /// <summary>Why the host cannot be said to have restarted the account, or <c>null</c> when it can.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns>Operator-facing English naming what was seen, or <c>null</c> when the restart is observed.</returns>
    /// <remarks>
    /// The readable-directory condition is checked here for the same reason it is checked on the way
    /// in: an unreadable directory produces an empty site list, and an empty list would satisfy "no
    /// vhost is still the stub" by having looked at nothing. A blind answer is never an observation
    /// in either direction, and that includes a single vhost the agent could not compare
    /// (<see cref="SiteServing.Unobservable"/>), which is refused here exactly as it is on the way in.
    /// </remarks>
    public static string? StillStopped(AccountSuspensionStateDto state)
    {
        if (!state.SitesDirectoryReadable)
        {
            return "the agent could not read the directory it lists vhosts from, so an empty answer "
                + "would mean 'it did not look' and not 'nothing is stubbed'";
        }

        var unobservable = state.Sites
            .Where(site => { return site.Serving == SiteServing.Unobservable; })
            .Select(site => { return site.Domain; })
            .OrderBy(domain => { return domain; }, StringComparer.Ordinal)
            .ToList();

        if (unobservable.Count > 0)
        {
            return $"these vhosts could not be compared with the suspension page: {string.Join(", ", unobservable)}";
        }

        var stubbed = state.Sites
            .Where(site => { return site.Serving == SiteServing.Stub; })
            .Select(site => { return site.Domain; })
            .OrderBy(domain => { return domain; }, StringComparer.Ordinal)
            .ToList();

        if (stubbed.Count > 0)
        {
            return $"these vhosts still serve the suspension page: {string.Join(", ", stubbed)}";
        }

        if (StillHeldDown(state))
        {
            return "the account's own login is still locked over a password that was not restored";
        }

        if (state.CronEntriesSuspended > 0)
        {
            return $"{state.CronEntriesSuspended} of the account's cron entries still carry the "
                + "suspension marker, so cron cannot run them";
        }

        // The one honest false refusal in this handler, and it is documented rather than papered
        // over: a login that never had a password cannot be unlocked — `usermod --unlock` refuses
        // that while exiting zero — so it reads locked here for ever. Every login the agent creates
        // is given a password in the same operation, so this can only be a hand-made login, and
        // refusing sends an operator to look at it instead of reporting a restoration that did not
        // happen.
        var locked = state.FileTransferLogins
            .Where(login => { return login.Locked; })
            .OrderBy(login => { return login.Username; }, StringComparer.Ordinal)
            .Select(TransferLoginProtocolPolicy.Describe)
            .ToList();

        return locked.Count > 0
            ? $"these transfer logins are still locked: {string.Join(", ", locked)}"
            : null;
    }

    /// <summary>Puts the attestation's own answer, and its standing blind spots, into one task line.</summary>
    /// <param name="state">What the host answered.</param>
    /// <returns>The line to report.</returns>
    /// <remarks>
    /// <para>
    /// It names what was NOT covered for the same reason suspension's does: the account's databases
    /// were never stopped, so a resumption reporting an unqualified
    /// restart would imply they had been. English and not localized, like every other task line
    /// (rules/csharp.md).
    /// </para>
    /// <para>
    /// It does NOT carry suspension's unmanaged-login clause, and that is a decision rather than an
    /// omission: a login the panel does not own was never locked by the suspension, so there is
    /// nothing about it for a resumption to have failed to restore. The number belongs on the line
    /// that claims something was stopped, which is the only place it can mislead.
    /// </para>
    /// </remarks>
    public static string Describe(AccountSuspensionStateDto state)
    {
        return $"the host shows the login unlocked, none of its {state.Sites.Count} vhosts for this "
            + $"account serving the suspension page, all {state.CronEntriesTotal} of its cron entries "
            + $"free to run again and {TransferLoginProtocolPolicy.DescribeAll(state.FileTransferLogins)} "
            + "unlocked; the account's databases were never stopped by the "
            + "suspension, so nothing here restored them";
    }

    /// <summary>Whether something of the customer's is still held down on the account's own login.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns><c>true</c> when the login carries a password the resumption failed to give back.</returns>
    /// <remarks>
    /// <para>
    /// This is the question a REACTIVATION has to ask, and until this method existed it was answered
    /// with the SUSPENSION question's answer. <c>passwd -S</c> — the source of the wire's older
    /// <c>login_locked</c> boolean — reports a login locked over a password
    /// and a login that never had one as the same thing, and every hosting account is the second
    /// kind, because the agent sets no password on an account's own entry. So the boolean was true
    /// for such an account for ever and this handler refused EVERY hosting account, on both
    /// families, even where the agent had unlocked exactly what there was to unlock.
    /// </para>
    /// <para>
    /// Only <see cref="AccountLoginPasswordState.Locked"/> is a real hold: a hash behind a lock
    /// marker, and the one state in which the agent's <c>usermod --unlock</c> has work to do, so
    /// seeing it after a resumption means the unlock did not happen.
    /// <see cref="AccountLoginPasswordState.Absent"/> is what a NEVER-SUSPENDED account looks like —
    /// there is nothing to give back and nothing was taken — and
    /// <see cref="AccountLoginPasswordState.Usable"/> is a login that authenticates today. Neither
    /// is a reason to refuse. <see cref="AccountLoginPasswordState.Empty"/> is not held down either;
    /// it is a login with no password at all, which suspension refuses to create and which this
    /// handler has no power to repair — refusing here would leave such an account permanently
    /// unreactivatable while doing nothing about the open door.
    /// </para>
    /// <para>
    /// An agent that predates the classification sends only the boolean, and the client resolves it
    /// to <see cref="AccountLoginPasswordState.Locked"/> when it reports a lock, which restores the
    /// OLD refusal — the defect — rather than inventing a permission from an answer nobody gave. A
    /// skewed pair is then no worse off than before and never more permissive; the handshake covers
    /// one release of skew (rules/proto.md).
    /// </para>
    /// </remarks>
    private static bool StillHeldDown(AccountSuspensionStateDto state)
    {
        return state.LoginPasswordState switch
        {
            AccountLoginPasswordState.Locked => true,
            AccountLoginPasswordState.Absent => false,
            AccountLoginPasswordState.Empty => false,
            AccountLoginPasswordState.Usable => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state.LoginPasswordState,
                "Unmapped login password state."),
        };
    }
}
