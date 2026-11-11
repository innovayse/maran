using Maran.Modules.Mail.Services;
using Maran.Sdk.Events;

namespace Maran.Modules.Mail.IntegrationEvents.Handlers;

/// <summary>
/// Pushes an account's local submission binding again when its plan figures changed
/// (<see cref="AccountPlanChanged"/>, issue #518), so the new hourly send limit reaches the filter now.
/// </summary>
/// <remarks>
/// <b>It never fails the plan change.</b> The event is announced after the commit, so a failure is logged by
/// code and returned from; the binding keeps the previous limit until the next push. The account is read as
/// the system, so a reseller-driven plan change binds too. A suspended account is skipped, because it stays
/// unbound until it resumes.
/// </remarks>
public sealed class AccountPlanChangedHandler
{
    /// <summary>Pushes the binding.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>Creates the handler.</summary>
    /// <param name="submission">Pushes the binding.</param>
    public AccountPlanChangedHandler(MailLocalSubmissionPublisher submission)
    {
        _submission = submission;
    }

    /// <summary>Pushes the account's binding with the new plan figures.</summary>
    /// <param name="message">The account whose plan figures changed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the binding was pushed, skipped or its failure logged.</returns>
    public async Task HandleAsync(AccountPlanChanged message, CancellationToken cancellationToken)
    {
        await _submission.TryPublishAfterChangeAsync(message.AccountId, cancellationToken);
    }
}
