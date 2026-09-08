using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Restores the sites an account had ENABLED when it is about to be reactivated
/// (<see cref="AccountResuming"/>), and leaves the ones the customer had disabled on the stub.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status filter is the whole point of this handler.</b> <see cref="AccountSuspendingHandler"/>
/// stubs every vhost the account owns and writes nothing, so after a suspension the host cannot tell
/// a site the customer disabled from a site the suspension disabled — both are serving the same
/// suspended vhost, byte for byte. Only <c>Site.Status</c> in this panel's own table records which
/// was which. So the reversal is asymmetric on purpose: everything is stubbed, and only
/// <see cref="SiteStatus.Enabled"/> comes back.
/// </para>
/// <para>
/// <b>Why the agent cannot do this instead.</b> It is the argument for orchestrating a suspension as
/// an event per module rather than as one agent rpc. The agent holds no <c>Site.Status</c> and has
/// no way to acquire it — a "resume everything" on the host would silently re-enable a site the
/// customer had turned off, which is the panel overwriting a customer's own decision while claiming
/// to restore it. Stated as the law it is an instance of: suspension must not overwrite state that
/// also expresses a customer choice, so resumption may only restore what suspension changed.
/// </para>
/// <para>
/// <b>The failure is not swallowed</b>, for the reason its sibling gives: anything thrown here
/// aborts the reactivation and leaves the account suspended, which is recoverable. An account marked
/// active whose sites still show the suspension page is the panel telling a paying customer their
/// service is back when it is not.
/// </para>
/// </remarks>
public sealed class AccountResumingHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The agent, which owns the vhost include directory.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="agent">The agent client that re-renders each vhost.</param>
    public AccountResumingHandler(SitesDbContext dbContext, IAgentSitesClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
    }

    /// <summary>Restores the site's own vhost for every site the account had enabled.</summary>
    /// <remarks>
    /// The tenant query filter is bypassed for the reason <see cref="AccountSuspendingHandler"/>
    /// gives: this is the reactivation of the account the rows belong to, authorised before the
    /// event was invoked, and a filtered query would silently restore nothing the day reactivation
    /// stops being an administrator's operation.
    /// </remarks>
    /// <param name="message">The account about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused to restore a vhost. Thrown rather than returned, because failing is a
    /// subscriber's only way to abort the reactivation.
    /// </exception>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being reactivated, so its rows must be found whoever asked for it
        var enabled = await _dbContext.Sites
            .IgnoreQueryFilters()
            .Where(site => site.AccountId == message.AccountId && site.Status == SiteStatus.Enabled)
            .OrderBy(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        foreach (var site in enabled)
        {
            var restored = await _agent.EnableAsync(
                message.Username, site.Domain, SiteDescriptorMapper.From(site), cancellationToken);
            if (!restored.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"the vhost of {site.Domain} could not be restored: {restored.Error?.Code}");
            }
        }
    }
}
