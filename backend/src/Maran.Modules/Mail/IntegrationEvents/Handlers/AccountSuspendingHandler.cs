using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Events;

namespace Maran.Modules.Mail.IntegrationEvents.Handlers;

/// <summary>
/// Suspends every mailbox of an account that is about to be suspended (<see cref="AccountSuspending"/>), so a
/// suspended customer stops being able to read, send and receive mail.
/// </summary>
/// <remarks>
/// <para>
/// <b>It writes no rows and reads none of its own.</b> The agent enumerates the account's mailboxes from the
/// HOST, not from this module's table: a table can only describe what the panel remembers creating, and a
/// mailbox it has forgotten is exactly the one that would keep working. The suspension also ends open IMAP and
/// submission sessions, because a lock alone ends neither.
/// </para>
/// <para>
/// <b>Nothing is deleted and no password is changed.</b> The resume gives the customer back the credentials they
/// already have. When an administrator has not turned mail on there is nothing on the host to suspend, and the
/// handler does nothing rather than ask the agent about a stack that does not exist.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler, which abandons
/// the suspension with the account left active — the recoverable direction. The number of mail sessions ended
/// is NOT written into <see cref="AccountSuspending.Report"/>: that report carries one session-cull reading,
/// which the Sftp module already owns, and a second writer would overwrite it.
/// </para>
/// <para>
/// <b>The local submission binding goes first</b> (issue #518): the account's system user can no longer submit
/// mail through <c>sendmail</c>, PHP or cron, a path the mailbox lock does not reach. The resumption pushes it
/// back, and so does a suspension that is abandoned after this step.
/// </para>
/// </remarks>
public sealed class AccountSuspendingHandler
{
    /// <summary>The agent, which owns the host's mail configuration.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Tells whether mail is turned on at all.</summary>
    private readonly MailServerReader _server;

    /// <summary>Removes the account's local submission binding.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that suspends the account's mail.</param>
    /// <param name="server">Reads whether an administrator turned mail on.</param>
    /// <param name="submission">Removes the account's local submission binding.</param>
    public AccountSuspendingHandler(
        IAgentMailClient agent,
        MailServerReader server,
        MailLocalSubmissionPublisher submission)
    {
        _agent = agent;
        _server = server;
        _submission = submission;
    }

    /// <summary>Suspends the account's mail on the host.</summary>
    /// <param name="message">The account about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused. Thrown rather than returned, because a subscriber's only way to abort the suspension
    /// is to fail. The agent's own text is logged by the client and never carried here (rules/security.md item 8).
    /// </exception>
    public async Task HandleAsync(AccountSuspending message, CancellationToken cancellationToken)
    {
        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            return;
        }

        // First, so that a suspended customer's cron and PHP can no longer submit mail by the time the
        // suspension completes. A refusal aborts the suspension like any other.
        var unbound = await _submission.RemoveAsync(message.Username, cancellationToken);
        if (!unbound.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the local mail submission of {message.Username} could not be removed: {unbound.Error?.Code}");
        }

        var suspended = await _agent.SetAccountSuspensionAsync(message.Username, true, cancellationToken);
        if (!suspended.IsSuccess)
        {
            // The suspension is abandoned with the account left active, so its binding goes back; if that
            // push fails too the ledger says it is out of date and the scheduled reconcile pushes it.
            await _submission.PublishAsSystemAsync(message.AccountId, cancellationToken);

            throw new InvalidOperationException(
                $"the mail of {message.Username} could not be suspended: {suspended.Error?.Code}");
        }
    }
}
