using Maran.Modules.Mail.Services;

namespace Maran.Modules.Mail.Jobs;

/// <summary>
/// Handles <see cref="MailSubmissionReconcileRequested"/>: pushes the bindings that are owed and removes the
/// ones that no longer belong (issue #518).
/// </summary>
/// <remarks>
/// This is what makes a push that failed after a committed change converge: the failure leaves the account's
/// ledger row saying the binding is out of date, and this pass pushes it again. It runs with no principal, so
/// it reads the accounts through the unscoped list, which is the unattended use that member documents. It
/// pushes only what is owed (<c>pushEveryAccount: false</c>); enabling mail is what pushes everything.
/// </remarks>
public sealed class MailSubmissionReconcileHandler
{
    /// <summary>Pushes and removes bindings.</summary>
    private readonly MailLocalSubmissionPublisher _submission;

    /// <summary>Creates the handler.</summary>
    /// <param name="submission">Pushes and removes bindings.</param>
    public MailSubmissionReconcileHandler(MailLocalSubmissionPublisher submission)
    {
        _submission = submission;
    }

    /// <summary>Runs one reconcile pass.</summary>
    /// <param name="message">The trigger.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    public async Task HandleAsync(MailSubmissionReconcileRequested message, CancellationToken cancellationToken)
    {
        await _submission.ReconcileAsync(pushEveryAccount: false, cancellationToken);
    }
}
