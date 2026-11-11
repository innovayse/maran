using Maran.Modules.Mail.Services;
using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.IntegrationEvents.Handlers;

/// <summary>
/// Binds the local mail submission of a newly created account (<see cref="AccountCreated"/>, issue #518), so
/// its cron jobs and PHP can send from the first domain it gets without waiting for another change.
/// </summary>
/// <remarks>
/// <para>
/// A new account owns no mail domain yet, so the binding it gets is the plan's send limit with no domains and
/// no mailboxes: it can send nothing locally until a domain is added, which pushes the binding again.
/// </para>
/// <para>
/// <b>It never fails the creation.</b> The account and its system user already exist; a push that cannot be
/// made is logged by code and returned from, and the account is simply unbound, the safe state. Mail that is
/// not turned on is skipped.
/// </para>
/// </remarks>
public sealed class AccountCreatedHandler
{
    /// <summary>Pre-compiled log delegate for an account whose binding could not be made.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogBindFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AccountCreatedHandler)),
            "The local mail submission of the new account {AccountId} could not be bound ({Code}); "
            + "it stays unbound until a mail domain or mailbox change pushes it.");

    /// <summary>Binds the account's local submission.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>Where a failure is recorded.</summary>
    private readonly ILogger<AccountCreatedHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="submission">Binds the account's local submission.</param>
    /// <param name="logger">Where a failure is recorded.</param>
    public AccountCreatedHandler(MailLocalSubmissionPublisher submission, ILogger<AccountCreatedHandler> logger)
    {
        _submission = submission;
        _logger = logger;
    }

    /// <summary>Pushes the new account's binding.</summary>
    /// <param name="message">The account that was created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the binding was pushed or its failure logged.</returns>
    public async Task HandleAsync(AccountCreated message, CancellationToken cancellationToken)
    {
        // The creation is already committed: a binding that cannot be made now (agent down, a fault
        // reading the mail ledger) is logged and left to the reconcile pass, never turned into a
        // failed creation of an account that exists.
        try
        {
            var bound = await _submission.PublishAsSystemAsync(message.AccountId, cancellationToken);
            if (!bound.IsSuccess)
            {
                LogBindFailed(_logger, message.AccountId, bound.Error!.Code, null);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogBindFailed(_logger, message.AccountId, exception.GetType().Name, exception);
        }
    }
}
