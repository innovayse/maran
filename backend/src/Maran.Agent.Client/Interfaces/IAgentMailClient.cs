using Maran.Agent.Client.Services.MailService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the host's mail stack: the switch that configures and runs the daemons, what
/// they are observed to do, and the domains, mailboxes and aliases they serve.
/// </summary>
/// <remarks>
/// <para>
/// A contract of its own, so that a module managing mail declares <c>AgentCapability.Mail</c> and gains
/// nothing over the firewall, certificate or customer-file clients. Mail does NOT open its ports: the
/// status reports what the daemons listen on and the operator, or the Firewall module, opens them.
/// </para>
/// <para>
/// <b>Nothing here names a path, a binary or a template.</b> The certificate is the panel's own, taken by
/// the agent from its fixed location; a mailbox directory is derived by the agent from the validated
/// account, domain and local part and created as the account's own uid. The request cannot point the
/// agent anywhere else, so the escape class is closed by construction rather than by a check.
/// </para>
/// <para>
/// <b>Secrets go one way.</b> The mailbox password hash, the DKIM private key and the webmail master hash
/// travel to the agent in non-printing wrappers, are unwrapped once onto the wire, and are never in any
/// result. A password is never sent in the clear: the panel hashes in the scheme
/// <see cref="AgentMailStatus.PasswordScheme"/> reports, and asks <see cref="GetStatusAsync"/> first.
/// </para>
/// <para>
/// <b>Every call is a closed, idempotent command.</b> The agent reports <c>AlreadyExists</c> or
/// <c>NotFound</c> in the failure branch and converges on a retry. Every mutating call is run to completion
/// by the agent even if this side stops waiting, because a half-written configuration of four daemons, or
/// a half-deleted mailbox, is the state nothing can reason about; the remedy for a lost answer is to ask
/// again.
/// </para>
/// </remarks>
public interface IAgentMailClient
{
    /// <summary>Reports the mail stack as it is now, read from the host and not from what was rendered.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The observed stack, or a typed failure. A host where mail was never enabled answers a status with
    /// everything <c>No</c> or <c>NotObserved</c>; that is a state, not an error.
    /// </returns>
    Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Renders the host's mail configuration, starts the daemons and reports what they do.</summary>
    /// <param name="settings">The panel's settings; the agent owns no default for any of them.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// What the host does afterwards, or a typed failure — <c>AgentValidationFailed</c> when a daemon's
    /// own checker refused the rendered configuration and the previous state was restored.
    /// </returns>
    /// <remarks>
    /// The one call that makes the host listen on mail ports. Idempotent: enabling an enabled stack
    /// converges it to the settings given.
    /// </remarks>
    Task<Result<AgentMailStatus>> EnableAsync(AgentMailServerSettings settings, CancellationToken cancellationToken);

    /// <summary>Stops the mail daemons and disables their units so nothing listens.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The observed stack afterwards, or a typed failure.</returns>
    /// <remarks>Data and per-account configuration are untouched, so a later enable brings every mailbox back. Idempotent.</remarks>
    Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken);

    /// <summary>Makes the daemons use the panel's current certificate, by reloading them and never restarting.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The observed stack afterwards, or a typed failure — <c>AgentNotFound</c> when mail is not enabled.</returns>
    Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken);

    /// <summary>Makes one domain deliverable and signable for an account, and installs its DKIM key.</summary>
    /// <param name="arguments">The account, the domain and the active DKIM selector and key.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// <c>true</c> when the host's state differed and was changed, <c>false</c> on an idempotent retry, or a
    /// typed failure — <c>AgentAlreadyExists</c> when the domain belongs to another account.
    /// </returns>
    /// <remarks>Rotating a key is calling this again with a new selector and key.</remarks>
    Task<Result<bool>> ConfigureDomainAsync(AgentMailDomainArguments arguments, CancellationToken cancellationToken);

    /// <summary>Withdraws a domain: its mail is no longer accepted, its DKIM key and rendered fragments are removed.</summary>
    /// <param name="accountUsername">System username of the account that owns the domain.</param>
    /// <param name="domain">The mail domain to withdraw.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when the account does not hold the domain.</returns>
    /// <remarks>Mailboxes and their mail are NOT deleted; the panel removes mailboxes first when it means to.</remarks>
    Task<Result> RemoveDomainAsync(string accountUsername, string domain, CancellationToken cancellationToken);

    /// <summary>Creates or updates one mailbox and its login entry.</summary>
    /// <param name="arguments">The mailbox's identity, credential and limits.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Whether the mailbox was created and whether its password was replaced, or a typed failure —
    /// <c>AgentNotFound</c> when the domain is not configured for the account, <c>AgentInvalidInput</c>
    /// for a creation without a password hash.
    /// </returns>
    Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken);

    /// <summary>Removes one mailbox's login and, when asked, its directory and every message in it.</summary>
    /// <param name="accountUsername">System username of the account the mailbox belongs to.</param>
    /// <param name="domain">The mail domain of the mailbox.</param>
    /// <param name="localPart">The local part of the mailbox.</param>
    /// <param name="deleteData">True deletes the mail data too; false keeps it, so a caller that forgot never destroys mail.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Whether the data was deleted by this call, or a typed failure — <c>AgentNotFound</c> when there is no such mailbox.</returns>
    Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken);

    /// <summary>Replaces ALL aliases, forwarders and the catch-all of one domain with exactly the given lists.</summary>
    /// <param name="accountUsername">System username of the account that owns the domain.</param>
    /// <param name="domain">The mail domain whose aliases are replaced.</param>
    /// <param name="aliases">Every alias of the domain; an empty list removes them all.</param>
    /// <param name="catchAllTargets">The catch-all's targets, or <c>null</c> for a domain with no catch-all.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>What the domain has now, or a typed failure.</returns>
    Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken);

    /// <summary>Installs, replaces or clears the out-of-office reply of one mailbox.</summary>
    /// <param name="accountUsername">System username of the account the mailbox belongs to.</param>
    /// <param name="domain">The mail domain of the mailbox.</param>
    /// <param name="localPart">The local part of the mailbox.</param>
    /// <param name="autoresponder">The reply to install, or <c>null</c> to clear the mailbox's autoresponder.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Whether the mailbox has an autoresponder after the call, or a typed failure — <c>AgentNotFound</c> when there is no such mailbox.</returns>
    Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken);

    /// <summary>Suspends or resumes ALL mail of one account, ending open sessions on a suspension.</summary>
    /// <param name="accountUsername">System username of the account whose mail is suspended or resumed.</param>
    /// <param name="suspended">True suspends; false resumes.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>What the agent found and ended, or a typed failure.</returns>
    /// <remarks>
    /// The mailboxes come from the host, not from a list the panel supplies: a list can only describe what
    /// the panel remembers creating. Idempotent in both directions; an account with no mail is a success.
    /// </remarks>
    Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken);

    /// <summary>Removes EVERYTHING mail of one account, before the account's system user is removed.</summary>
    /// <param name="accountUsername">System username of the account whose mail is removed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>What was taken off the host, or a typed failure.</returns>
    /// <remarks>
    /// Called by account deletion BEFORE the user is removed, so an account later handed the same uid never
    /// inherits another customer's mail. Idempotent: an account with no mail answers zeros.
    /// </remarks>
    Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken);

    /// <summary>Measures every mailbox of one account.</summary>
    /// <param name="accountUsername">System username of the account whose mailboxes are measured.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// One entry per mailbox the host holds; a mailbox that could not be measured has no reading, which is
    /// not an empty mailbox.
    /// </returns>
    Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken);

    /// <summary>Lists the host's mail queues, bounded.</summary>
    /// <param name="limit">The most entries to return; zero means the agent's default, and the agent caps it.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The entries and the total, or a typed failure. Senders in the entries are untrusted text.</returns>
    Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken);

    /// <summary>Removes one queued message by its id.</summary>
    /// <param name="queueId">The MTA's queue id; revalidated by the agent.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when no such message is queued.</returns>
    Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken);

    /// <summary>Asks the MTA to attempt delivery of every deferred message now; nothing is deleted.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The queue size after the flush was requested, <c>null</c> when it was not read, or a typed failure.</returns>
    Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken);

    /// <summary>Binds mail an account submits locally to the account's own domains and hourly send limit.</summary>
    /// <param name="submission">The complete binding; it replaces whatever the host held for the account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentValidationFailed</c> when the agent refused a figure.</returns>
    /// <remarks>Idempotent: sending the same binding again converges and changes nothing.</remarks>
    Task<Result> SetLocalSubmissionAsync(AgentLocalSubmission submission, CancellationToken cancellationToken);

    /// <summary>Removes the account's local submission binding, so its system user can no longer submit mail locally.</summary>
    /// <param name="accountUsername">System username of the account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure. An account with no binding is a success.</returns>
    Task<Result> RemoveLocalSubmissionAsync(string accountUsername, CancellationToken cancellationToken);
}
