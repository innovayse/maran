using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Rewrites an account's php-fpm pools when its plan figures changed, and queues the render of its PHP vhosts
/// when the plan's upload bound moved, so the new bounds reach them now and not at the next panel start
/// (<see cref="AccountPlanChanged"/>, issues #138 and #556).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A pool carries the customer's settings cut to the plan's bound, and a plan
/// edit changes the bound. Without this the pool would keep the old figure until something else wrote
/// it — a site operation, or the next start. <c>max_children</c> had exactly that lag, and the same
/// pool write closes it for the worker budget too.
/// </para>
/// <para>
/// <b>It never fails the plan change.</b> The event is announced after the commit, so a failure here is
/// logged and returned from, not thrown: the row is written and nothing a subscriber does can undo it.
/// What it misses the start-up pass converges (<see cref="StartupPhpSiteReconciler"/>), because both
/// read the same stored settings and cut them to the same bound.
/// </para>
/// <para>
/// <b>The vhosts follow the plan too, in the background.</b> A PHP site's request body limit is the plan's
/// upload bound unless its owner stated one (<see cref="Domain.Policies.SiteBodySizePolicy"/>), so a changed
/// bound changes what the vhost must carry. That is one agent call per site, so it is not done here, inside the
/// administrator's request: this handler publishes <see cref="PhpSiteVhostsRerenderRequested"/> and
/// <see cref="PhpSiteVhostsRerenderRequestedHandler"/> renders them. It does so only when
/// <see cref="AccountPlanChanged.UploadBoundChanged"/> says the bound moved; the plan's other figures change
/// pools alone. A disabled site's stub carries no options and takes the figure when it is enabled.
/// </para>
/// <para>
/// An account with no PHP site has no pool and nothing to write. The previous pool contents are not
/// known here, so a failure part-way is not compensated: some versions may carry the new bound and
/// others the old until the next start.
/// </para>
/// </remarks>
public sealed class AccountPlanChangedHandler
{
    /// <summary>Pre-compiled log delegate for an account whose plan figures are no longer readable.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAccountMissing =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(AccountPlanChangedHandler)),
            "Skipped the pools of account {AccountId} after its plan changed: the account is not readable");

    /// <summary>Pre-compiled log delegate for a pool the agent would not rewrite.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogApplyFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2, nameof(AccountPlanChangedHandler)),
            "Could not rewrite the PHP pools of account {AccountId} after its plan changed: {Code}");

    /// <summary>The account's system user, worker budget and plan bounds.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Which versions the account has pools at, and what its pools carry.</summary>
    private readonly PhpAccountPoolSettings _pools;

    /// <summary>Rewrites those pools.</summary>
    private readonly PhpPoolApplier _applier;

    /// <summary>Queues the render of the account's vhosts.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a failure is recorded.</summary>
    private readonly ILogger<AccountPlanChangedHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The account's system user, worker budget and plan bounds.</param>
    /// <param name="pools">Which versions the account has pools at, and what its pools carry.</param>
    /// <param name="applier">Rewrites those pools.</param>
    /// <param name="bus">Queues the render of the account's vhosts.</param>
    /// <param name="logger">Where a failure is recorded.</param>
    public AccountPlanChangedHandler(
        IAccountDirectory accounts,
        PhpAccountPoolSettings pools,
        PhpPoolApplier applier,
        IMessageBus bus,
        ILogger<AccountPlanChangedHandler> logger)
    {
        _accounts = accounts;
        _pools = pools;
        _applier = applier;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>Rewrites the account's pools for the new bounds and queues its vhosts when the upload bound moved.</summary>
    /// <param name="message">The account whose plan figures changed.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes when every pool was written or one refused, and the vhosts are queued.</returns>
    public async Task HandleAsync(AccountPlanChanged message, CancellationToken cancellationToken)
    {
        // Read through the directory of the request that announced the change: an administrator or the
        // reseller who owns the account, both of whom the directory answers for.
        var account = await _accounts.FindAsync(message.AccountId, cancellationToken);
        if (account is null)
        {
            LogAccountMissing(_logger, message.AccountId, null);
            return;
        }

        var versions = await _pools.VersionsInUseAsync(account.Id, cancellationToken);
        if (versions.Count == 0)
        {
            return;
        }

        var settings = await _pools.EffectiveAsync(account.Id, account.PhpLimits, cancellationToken);
        var applied = await _applier.ApplyAsync(
            account.Username, account.MaxPhpWorkersPerPool, versions, settings, previous: null, cancellationToken);
        if (!applied.IsSuccess)
        {
            LogApplyFailed(_logger, account.Id, applied.Error!.Code, null);
        }

        if (message.UploadBoundChanged)
        {
            await _bus.PublishAsync(new PhpSiteVhostsRerenderRequested(account.Id));
        }
    }
}
