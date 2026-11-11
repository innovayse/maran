using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Events;

namespace Maran.Modules.Mail.IntegrationEvents.Handlers;

/// <summary>
/// Resumes every mailbox of an account that is being reactivated (<see cref="AccountResuming"/>), giving the
/// customer back the credentials they already have.
/// </summary>
/// <remarks>
/// The mirror of <see cref="AccountSuspendingHandler"/>, with the same reasons: the mailboxes are the host's to
/// enumerate, nothing is stored here, mail that was never enabled is skipped, and a refusal by the agent is
/// thrown so the Accounts handler abandons the reactivation. It also puts the local submission binding back
/// (issue #518), built from the account's current rows and plan.
/// </remarks>
public sealed class AccountResumingHandler
{
    /// <summary>The agent, which owns the host's mail configuration.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Tells whether mail is turned on at all.</summary>
    private readonly MailServerReader _server;

    /// <summary>Puts the account's local submission binding back.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that resumes the account's mail.</param>
    /// <param name="server">Reads whether an administrator turned mail on.</param>
    /// <param name="submission">Puts the account's local submission binding back.</param>
    public AccountResumingHandler(
        IAgentMailClient agent,
        MailServerReader server,
        MailLocalSubmissionPublisher submission)
    {
        _agent = agent;
        _server = server;
        _submission = submission;
    }

    /// <summary>Resumes the account's mail on the host.</summary>
    /// <param name="message">The account being reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">The agent refused.</exception>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            return;
        }

        var resumed = await _agent.SetAccountSuspensionAsync(message.Username, false, cancellationToken);
        if (!resumed.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the mail of {message.Username} could not be resumed: {resumed.Error?.Code}");
        }

        var bound = await _submission.PublishAsSystemAsync(message.AccountId, cancellationToken);
        if (!bound.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the local mail submission of {message.Username} could not be restored: {bound.Error?.Code}");
        }
    }
}
