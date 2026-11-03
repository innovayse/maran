using Maran.Agent.Client.Services.AccountsService;
using Maran.Sdk.Events;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// How a suspension reads what the host answered about an account, and words it for the task
/// journal: the stateless half of <c>SuspendAccountCommandHandler</c>, kept apart so the handler
/// holds the orchestration and this holds the rules over the host's answer.
/// </summary>
/// <remarks>
/// Moved out of the handler unchanged. What a suspension covers, what it deliberately does not, and
/// what COMPLETED is allowed to mean are stated on the handler's <c>HandleAsync</c>.
/// </remarks>
public static class SuspensionAttestationPolicy
{
    /// <summary>Why the host cannot be said to have stopped the account, or <c>null</c> when it can.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns>Operator-facing English naming what was seen, or <c>null</c> when the stop is observed.</returns>
    /// <remarks>
    /// <para>
    /// Three conditions, and the first is the one that would otherwise make this check decoration.
    /// An unreadable vhost directory and an account with no vhost at all produce the same empty
    /// list, and the empty list is the one that reads as "everything is suspended" — so a blind
    /// answer must never be accepted as a suspended one. The other two are the facts themselves:
    /// every vhost the host holds is the suspended one, and the account's own login is locked.
    /// </para>
    /// <para>
    /// The site set is the HOST's and not the panel's, deliberately. A vhost the panel has forgotten
    /// creating is exactly the one still serving a suspended customer's page, so this refuses on
    /// vhosts no <c>Site</c> row names. That can only fail in the safe direction: it refuses a
    /// suspension the panel believes complete, and never certifies one.
    /// </para>
    /// </remarks>
    public static string? Unobserved(AccountSuspensionStateDto state)
    {
        if (!state.SitesDirectoryReadable)
        {
            return "the agent could not read the directory it lists vhosts from, so an empty answer "
                + "would mean 'it did not look' and not 'nothing is serving'";
        }

        var serving = state.Sites
            .Where(site => { return site.Serving != SiteServing.Stub; })
            .Select(site => { return site.Domain; })
            .OrderBy(domain => { return domain; }, StringComparer.Ordinal)
            .ToList();

        if (serving.Count > 0)
        {
            // Both NotStub and Unobservable land here: a vhost whose comparison could not be made
            // is not an observation of the suspended page, so a suspension is never certified over it.
            return $"these vhosts are not observed serving the suspended one: {string.Join(", ", serving)}";
        }

        if (CanStillAuthenticate(state))
        {
            return "the account's own login can still be authenticated with a password";
        }

        // Counted out of the crontab itself, because this panel keeps no cron rows: the Cron module
        // owns no entity, so a check that consulted the database would be green over a firing
        // crontab. Foreign lines are NOT part of this condition — the panel does not silence them
        // and refusing on them would make an account with one hand-written cron line permanently
        // unsuspendable; they are reported on the task instead.
        if (state.CronEntriesSuspended != state.CronEntriesTotal)
        {
            var firing = state.CronEntriesTotal - state.CronEntriesSuspended;

            return $"{firing} of the account's {state.CronEntriesTotal} cron entries can still be run by cron";
        }

        // Enumerated from the host's password database and not from the rows of the modules that
        // create these logins: a login the panel has forgotten is exactly the one still letting a
        // suspended customer write to their home, so this refuses on logins no row names. Like the
        // vhost set, that can only fail in the safe direction. The list holds both daemons' logins,
        // so each name is printed with the daemon it belongs to — an operator sent to look at
        // "acme_web" needs to know which client to open.
        var open = state.FileTransferLogins
            .Where(login => { return !login.Locked; })
            .OrderBy(login => { return login.Username; }, StringComparer.Ordinal)
            .Select(TransferLoginProtocolPolicy.Describe)
            .ToList();

        return open.Count > 0
            ? $"these transfer logins still authenticate: {string.Join(", ", open)}"
            : null;
    }

    /// <summary>Puts the attestation's own answer, and its standing blind spots, into one task line.</summary>
    /// <param name="state">What the host answered.</param>
    /// <param name="report">What the suspension cascade's subscribers reported back.</param>
    /// <returns>The line to report.</returns>
    /// <remarks>
    /// <para>
    /// This line is the only place the operator is told that anything was CHECKED, and the only
    /// place they are told what was not. A pane reading as three stages of doing and no stage of
    /// looking is how a completed task came to be believed over a site that was still serving.
    /// </para>
    /// <para>
    /// The qualification is not optional prose. What a suspension does not reach — the account's
    /// databases, any crontab line the panel did not write, and any login
    /// sharing the account's uid that the panel did not create — is named on the line, because a
    /// completion that claimed more than it observed is the defect this whole attestation exists to
    /// end. The last two are the ones the host can actually measure, so they are reported as numbers
    /// rather than as caveats; <see cref="UnmanagedLoginPolicy"/> holds the reading of the second,
    /// which is the only figure here that can be absent as well as zero.
    /// </para>
    /// <para>
    /// The locked logins are counted per DAEMON, not as one total under one daemon's name. The
    /// host's list has carried FTPS logins beside the SFTP ones since FTPS shipped, and this sentence
    /// said "sftp logins" about all of them — which is wrong in the direction that matters, because
    /// an operator reading it believes the customer's FTP credentials were never in scope when in
    /// fact they were locked. <see cref="TransferLoginProtocolPolicy"/> words the counts, and names
    /// BOTH daemons whatever their counts: a daemon dropped for having no logins reproduced that same
    /// misreading on every account with no logins at all, which is the commonest account there is.
    /// </para>
    /// <para>
    /// One clause on this line comes from the CASCADE and not from the host's answer, and it is the
    /// only one that does. The suspension ends the account's open transfer sessions — a privileged
    /// action, because a transfer in flight is cut and the partial file stays — and a cull is an event
    /// with no afterwards, so <c>GetAccountSuspensionState</c> cannot be asked about it the way every
    /// other fact here is. The subscriber that performs it reports the agent's own count into
    /// <see cref="AccountSuspending.Report"/>, and <see cref="SessionCullPolicy"/> words the four
    /// readings that can arrive: a number, a measured none, a count the host did not give, and no
    /// module having reported at all. An earlier version of this line could only say that the panel
    /// ASKED, because the rpc's success message was empty; the operator was meanwhile warned about the
    /// cost before confirming, so the promise had nothing after it.
    /// </para>
    /// <para>
    /// English and not localized, like every other line on a task: it is read by the operator who
    /// administers the server (rules/csharp.md).
    /// </para>
    /// </remarks>
    public static string Describe(
        AccountSuspensionStateDto state,
        AccountSuspensionCascadeReport report)
    {
        var foreign = state.CronForeignLines == 0
            ? string.Empty
            : $", and {state.CronForeignLines} crontab line(s) the panel did not write, which it does "
                + "not touch and which keep firing";

        return $"the host shows the login locked, all {state.Sites.Count} of its vhosts for this account "
            + $"serving the suspended page, all {state.CronEntriesTotal} of its cron entries suppressed "
            + $"and {TransferLoginProtocolPolicy.DescribeAll(state.FileTransferLogins)} locked; "
            + SessionCullPolicy.Describe(report)
            + $"NOT covered by this suspension: the account's databases{foreign}"
            + UnmanagedLoginPolicy.Describe(state);
    }

    /// <summary>Whether a password could still authenticate the account's own login.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns><c>true</c> when a suspension has not stopped the login.</returns>
    /// <remarks>
    /// <para>
    /// This is the question a SUSPENSION has to ask, and it is not the one reactivation asks. Two
    /// shadow states can authenticate — a usable hash, and an EMPTY field, which authenticates with
    /// the empty password — and both must refuse a suspension. Every other state cannot, whether it
    /// holds a hash behind a lock marker or no hash at all.
    /// </para>
    /// <para>
    /// An agent that predates the classification sends only <c>passwd -S</c>'s boolean, and the
    /// client turns it into one of these four states before it reaches here, so there is no
    /// fallback to write: <c>passwd -S</c> reports a login with no hash and a login locked over a
    /// hash as the same thing, and neither of them can authenticate, which is the direction this
    /// question asks in. It is the OTHER direction, the one reactivation asks about, where
    /// collapsing them is fatal.
    /// </para>
    /// </remarks>
    private static bool CanStillAuthenticate(AccountSuspensionStateDto state)
    {
        return state.LoginPasswordState switch
        {
            AccountLoginPasswordState.Empty => true,
            AccountLoginPasswordState.Usable => true,
            AccountLoginPasswordState.Absent => false,
            AccountLoginPasswordState.Locked => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state.LoginPasswordState,
                "Unmapped login password state."),
        };
    }
}
