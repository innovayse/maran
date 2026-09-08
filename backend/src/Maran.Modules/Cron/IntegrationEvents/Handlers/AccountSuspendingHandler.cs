using Maran.Agent.Client.Interfaces;
using Maran.Sdk.Events;

namespace Maran.Modules.Cron.IntegrationEvents.Handlers;

/// <summary>
/// Suppresses every entry in the crontab of an account that is about to be suspended
/// (<see cref="AccountSuspending"/>), so a suspended customer's scheduled jobs stop running.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Suspending an account locked its Linux login, and cron does not consult a
/// login shell to run a job. Every entry the customer had installed went on firing on schedule while
/// the panel told billing the account was stopped — writing to their database, sending their
/// requests, filling their disk.
/// </para>
/// <para>
/// <b>It holds no rows, and cannot.</b> This module owns no entity: the account's crontab on the
/// host IS the record, which is why one call replaces the loop every other cascade handler runs. It
/// is also why a database-side check of a suspension would be green over a firing crontab, and why
/// the attestation the Accounts handler runs counts entries out of the crontab itself.
/// </para>
/// <para>
/// <b>The call is deliberately not a loop over the per-entry enablement.</b> That flag is the
/// CUSTOMER's own switch. If the suspension wrote to it, the resume would have nothing left to tell
/// an entry the customer had turned off from one the suspension turned off, and every job they had
/// disabled would come back running — with no second copy of that choice anywhere, because this
/// module keeps none. The agent therefore carries a second marker of its own, orthogonal to that
/// flag, and this call is the only thing that writes it.
/// </para>
/// <para>
/// <b>What it does not stop.</b> Lines in the crontab the panel did not write — added by an account
/// with shell access or by an administrator — are left exactly as they are, because a crontab is not
/// the panel's file and deleting somebody's hand-written job is not a suspension's business. They
/// keep firing, and the suspension state counts them so the panel reports the number rather than
/// claiming a silence it did not achieve.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the suspension with the account left active — the recoverable direction, because
/// an account still active can be suspended again, while an account marked suspended whose jobs are
/// still firing is a lie the panel has already told billing.
/// </para>
/// </remarks>
public sealed class AccountSuspendingHandler
{
    /// <summary>The agent, which owns the account's crontab.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that rewrites the account's crontab.</param>
    public AccountSuspendingHandler(IAgentCronClient agent)
    {
        _agent = agent;
    }

    /// <summary>Marks every managed entry of the account's crontab as suspended.</summary>
    /// <param name="message">The account about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused. Thrown rather than returned, because a subscriber's only way to abort the
    /// suspension is to fail. The agent's own text is carried in the exception, which the Accounts
    /// handler logs and never answers a customer with (rules/security.md item 8).
    /// </exception>
    public async Task HandleAsync(AccountSuspending message, CancellationToken cancellationToken)
    {
        var suspended = await _agent.SetAccountSuspendedAsync(message.Username, true, cancellationToken);
        if (!suspended.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the crontab of {message.Username} could not be suspended: {suspended.Error?.Code}");
        }
    }
}
