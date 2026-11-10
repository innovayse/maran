using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Dns.IntegrationEvents.Handlers;

/// <summary>
/// Takes an account's zones off the name server and removes their rows when the account is about to be
/// deleted (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all, and why it calls the agent.</b> Deleting an account does not, on its own,
/// remove a zone row, and a zone row removed alone leaves the zone served: the name server would go on
/// answering the internet for a name no account owns, and the next customer to claim the domain would find
/// the previous tenant's records live under it. Nothing else in the account teardown knows DNS, so the
/// removal from the host happens here, before the rows go, and a zone the agent no longer serves is
/// accepted as already removed.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here, or any agent refusal turned into an exception
/// below, propagates to the Accounts handler, which abandons the deletion with the account intact — the
/// recoverable half. A server that never stored a zone for the account does not touch the agent at all.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The agent, which owns the zone files and the daemon.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="agent">The agent client that removes the zones from the host.</param>
    public AccountDeletingHandler(DnsDbContext dbContext, IAgentDnsClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
    }

    /// <summary>Removes every zone of the account being removed, from the host and then from the database.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed: the filter governs what a REQUEST may see, and this
    /// is the removal of the account the rows belong to, authorised before the event was published.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its zones must be found whoever asked for the deletion
        var owned = await _dbContext.Zones
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .Include(row => row.SigningKeys)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        foreach (var zone in owned)
        {
            var removed = await _agent.DeleteZoneAsync(zone.Name, cancellationToken);
            if (!removed.IsSuccess && !string.Equals(removed.Error!.Code, AgentErrorCodes.NotFound, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The name server refused to drop a zone of account {message.AccountId} ({removed.Error.Code}).");
            }
        }

        _dbContext.Zones.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
