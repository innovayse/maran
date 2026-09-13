using Maran.Agent.Client.Interfaces;
using Maran.Sdk.Events;

namespace Maran.Modules.Cron.IntegrationEvents.Handlers;

/// <summary>
/// Lets the crontab of an account that is about to be reactivated run again
/// (<see cref="AccountResuming"/>), and gives back exactly the entries the customer had running.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reversal is exact because the suspension never overwrote anything.</b>
/// <see cref="AccountSuspendingHandler"/> writes a marker that is orthogonal to the per-entry
/// enablement, so removing it here restores each entry to whatever the customer had chosen: a job
/// they had switched off stays off, and one they had running runs again. There is no list to
/// consult and none is needed — which matters more here than anywhere else in the cascade, because
/// this module holds no rows and a list is the one thing it could not have kept.
/// </para>
/// <para>
/// It is deliberately unconditional where the Sites module's counterpart is selective. Sites must
/// filter by <c>Site.Status</c> because stubbing a vhost destroys the distinction on the host; cron
/// need not, because the two facts are two different markers on the same line and the customer's is
/// still there.
/// </para>
/// <para>
/// <b>The failure is not swallowed</b>, for the reason its sibling gives: anything thrown here
/// aborts the reactivation and leaves the account suspended, which is recoverable. An account marked
/// active whose scheduled jobs never restarted is the panel telling a paying customer their service
/// is back when part of it is not.
/// </para>
/// </remarks>
public sealed class AccountResumingHandler
{
    /// <summary>The agent, which owns the account's crontab.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that rewrites the account's crontab.</param>
    public AccountResumingHandler(IAgentCronClient agent)
    {
        _agent = agent;
    }

    /// <summary>Clears the suspension marker from the account's crontab.</summary>
    /// <param name="message">The account about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused. Thrown rather than returned, for the reason its sibling gives.
    /// </exception>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
        var resumed = await _agent.SetAccountSuspendedAsync(message.Username, false, cancellationToken);
        if (!resumed.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the crontab of {message.Username} could not be resumed: {resumed.Error?.Code}");
        }
    }
}
