using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Models;
using Maran.Modules.Mail.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Reads the alias rows of one domain and sends the agent the complete set — the one path by which an
/// alias, a forwarder or a catch-all reaches the host, shared by creating, changing and deleting one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the whole set, every time.</b> The agent's alias call REPLACES everything the domain has, so a
/// call carrying only the new address would erase every other one. The set is therefore always rebuilt
/// from the database, never from what the request mentions.
/// </para>
/// <para>
/// <b>Restoring.</b> The agent runs before the row is written, so a row that cannot be written leaves the
/// host holding an address the database does not know. The caller sends the set it held BEFORE the change
/// back through <see cref="RestoreAsync"/>. A restore that fails is logged with the domain and the agent's
/// error CODE and not surfaced, because the customer is already being told the change failed and can do
/// nothing about the second failure — but an operator can, if the line is there to find.
/// </para>
/// </remarks>
public sealed class MailAliasSetPublisher
{
    /// <summary>Pre-compiled log delegate for a restore that did not take.</summary>
    /// <remarks>
    /// Names the domain and the agent's error code, never the agent's text or any address: what remains is
    /// a host whose aliases disagree with the panel's rows, which only an operator can reconcile.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(MailAliasSetPublisher)),
            "The aliases of mail domain {MailDomain} could not be written and could not be restored either "
            + "({AgentErrorCode}); the host's aliases for it may differ from the panel's rows.");

    /// <summary>The Mail module's context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Where a failed restore is reported.</summary>
    private readonly ILogger<MailAliasSetPublisher> _logger;

    /// <summary>Creates the publisher.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="agent">The agent client that holds the host's aliases.</param>
    /// <param name="logger">Where a failed restore is reported.</param>
    public MailAliasSetPublisher(MailDbContext dbContext, IAgentMailClient agent, ILogger<MailAliasSetPublisher> logger)
    {
        _dbContext = dbContext;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Reads every alias row of a domain without tracking it, targets included.</summary>
    /// <param name="mailDomainId">The domain.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows the caller's tenant scope shows for that domain.</returns>
    public async Task<IReadOnlyList<MailAlias>> LoadSetAsync(Guid mailDomainId, CancellationToken cancellationToken)
    {
        return await _dbContext.MailAliases
            .AsNoTracking()
            .Include(alias => alias.Targets)
            .Where(alias => alias.MailDomainId == mailDomainId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads every alias row of a domain for change, targets included.</summary>
    /// <param name="mailDomainId">The domain.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tracked rows the caller's tenant scope shows for that domain.</returns>
    public async Task<IReadOnlyList<MailAlias>> LoadTrackedSetAsync(
        Guid mailDomainId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.MailAliases
            .Include(alias => alias.Targets)
            .Where(alias => alias.MailDomainId == mailDomainId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads the names of the mail domains one account owns.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The domain names; they decide which targets stay inside the account.</returns>
    public async Task<IReadOnlyCollection<string>> AccountDomainNamesAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        // The AccountId predicate is not decoration: an administrator's context is not narrowed by the
        // tenant filter, and "this account's domains" must not become "every account's" for them.
        return await _dbContext.MailDomains
            .AsNoTracking()
            .Where(domain => domain.AccountId == accountId)
            .Select(domain => domain.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Sends the agent the complete alias set of a domain.</summary>
    /// <param name="accountUsername">System username of the account that owns the domain.</param>
    /// <param name="domainName">The mail domain.</param>
    /// <param name="set">Everything the domain should have afterwards.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's typed failure.</returns>
    public async Task<Result> PublishAsync(
        string accountUsername,
        string domainName,
        MailAliasSet set,
        CancellationToken cancellationToken)
    {
        var outcome = await _agent.SetAliasesAsync(
            accountUsername, domainName, set.Aliases, set.CatchAllTargets, cancellationToken);

        return outcome.IsSuccess ? Result.Ok() : Result.Fail(outcome.Error!);
    }

    /// <summary>Sends the agent a set it held before a change that could not be recorded.</summary>
    /// <param name="accountUsername">System username of the account that owns the domain.</param>
    /// <param name="domainName">The mail domain.</param>
    /// <param name="set">The set to put back.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task RestoreAsync(
        string accountUsername,
        string domainName,
        MailAliasSet set,
        CancellationToken cancellationToken)
    {
        var restored = await PublishAsync(accountUsername, domainName, set, cancellationToken);
        if (!restored.IsSuccess)
        {
            LogRestoreFailed(_logger, domainName, restored.Error!.Code, null);
        }
    }
}
