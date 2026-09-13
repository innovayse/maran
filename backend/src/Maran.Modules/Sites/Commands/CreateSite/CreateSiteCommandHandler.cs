using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Interfaces;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Commands.CreateSite;

/// <summary>
/// Handles <see cref="CreateSiteCommand"/>: refuses what the plan does not allow, provisions the
/// site on the host through the agent, and only then records the row that defines it (spec §11).
/// </summary>
/// <remarks>
/// The order is deliberate and is the same one the accounts handlers use. The plan limit is checked
/// FIRST, before the agent is called at all: a site the plan refuses must never reach the host, or
/// the panel has created something it will then have to remember to remove. The agent runs SECOND
/// and the row is written only if it succeeded; the two stores can still disagree if the database
/// write fails afterwards, and this order decides WHICH way. A vhost with no row is invisible and
/// harmless, and creating the site again converges because the agent's operations are idempotent.
/// The reverse — a row the panel shows as a live site with no vhost behind it — is a customer told
/// they have a site that does not answer.
///
/// <b>The limit is checked twice, and the SECOND check is the one that holds.</b> The pre-agent check
/// is count-then-insert with nothing behind the count, so on its own two concurrent creations both read
/// N-1 and both succeed, leaving the account one site over its plan. It is kept because it refuses the
/// ordinary over-limit request before the host is touched at all. What closes the window is
/// <see cref="ISiteSlotGate"/>: the row is written under a per-account advisory lock, with the count
/// re-taken inside it, so the second of two simultaneous creations sees the first one's committed row
/// and is refused. The gate's own documentation says what each rejected alternative — a unique index,
/// an insert carrying the count, a serializable transaction, a counter column — is blind to, and why the
/// agent contributes nothing here. The DOMAIN race is the other kind and stays closed by the hostname
/// key, because a domain is a value and a key can refuse a repeated value.
///
/// <b>The loser pays for it on the host, which is why the pre-check stays.</b> The gate runs after the
/// agent has provisioned the site, so a request refused there leaves a vhost nothing owns, and it is
/// removed again. The compensating delete retires NO php-fpm pool — it passes the empty version, which
/// the agent documents as "leave every pool alone" — because a pool belongs to an ACCOUNT and a version
/// rather than to one site, and the account's other sites on that version share it. An orphaned pool is
/// wasteful and an operator can see it; a removed shared pool takes another of the customer's sites off
/// the air, so the conservative direction is the only defensible one for a compensation.
///
/// Every refusal is journalled as well as every success: a plan limit hit, a taken domain, an
/// account the caller may not see and an agent that said no are exactly the events an operator
/// later needs to explain what happened (<see cref="AuditEntry"/>).
/// </remarks>
public sealed class CreateSiteCommandHandler
{
    /// <summary>Customer php.ini overrides, of which the panel stores none in this pass.</summary>
    /// <remarks>
    /// NOT a fabricated value standing in for something the panel knows: there is no override
    /// storage in this module, so "none" is the true and complete set. It is passed all the same,
    /// because the pool the agent writes belongs to an ACCOUNT and a version rather than to one
    /// site — so the moment overrides gain a home, a creation that omitted them would rewrite an
    /// existing pool without the settings a customer had already set.
    /// </remarks>
    private static readonly IReadOnlyList<PhpSettingDto> NoSettingOverrides = [];

    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    /// <remarks>
    /// Source-generated because what it reports is an orphaned vhost on the host that only an operator
    /// can clear, and a message an operator has to find must be searchable and structured rather than
    /// interpolated. It names the site's domain and the agent's own error CODE, never the agent's text
    /// and never a path.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateSiteCommandHandler)),
            "Provisioned site {Domain} could not be recorded and could not be removed either "
            + "({AgentErrorCode}); its vhost is now on the host with no row.");

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything about the site that lives on the host.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>The host's PHP runtimes, so a site is never bound to a version that is not installed.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>The atomic claim on the account's last free slot; see the race paragraphs above.</summary>
    private readonly ISiteSlotGate _slotGate;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateSiteCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the site.</param>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    /// <param name="slotGate">Takes the account's last free slot atomically when the row is written.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new site's creation time.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateSiteCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        IAgentPhpClient php,
        ISiteSlotGate slotGate,
        SiteAuditJournal journal,
        IClock clock,
        ILogger<CreateSiteCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _php = php;
        _slotGate = slotGate;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Creates the site, refusing it before the host is touched when the plan or the domain says no.</summary>
    /// <param name="command">The validated site parameters; see <see cref="CreateSiteCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The created site, or <c>AccountNotFound</c>, <c>SiteLimitReached</c>,
    /// <c>SiteLimitReachedConcurrently</c>, <c>SiteDomainTaken</c>, <c>PhpVersionNotInstalled</c>, or
    /// the agent's own typed failure.
    /// </returns>
    public async Task<Result<SiteDto>> HandleAsync(CreateSiteCommand command, CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id is refused here and reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // Spec §8: countable limits are enforced in the application at creation time, BEFORE the
        // agent is called.
        //
        // The explicit AccountId predicate is what scopes this count, and the tenant filter is left
        // ON. An earlier version added IgnoreQueryFilters() here with a comment claiming it stopped
        // the limit becoming unbounded for an administrator; that was false — an administrator is
        // already unfiltered and a customer has just been proved by the directory to own this very
        // account, so the bypass was a no-op in every reachable case and no test could tell it from
        // its own absence. It is gone rather than kept as defensive-looking decoration.
        //
        // This check is count-then-insert with nothing behind the count, so on its own two concurrent
        // creates both read N-1 and both insert. It is kept because it refuses the ordinary
        // over-limit request before the host is touched at all; what CLOSES the window is
        // ISiteSlotGate, which re-takes the count under a per-account advisory lock at the moment the
        // row is written. The Domain unique index (SiteConfiguration) closes the equivalent race for
        // domains because a domain is a single value a UNIQUE can cover; a per-account COUNT is not,
        // which is the whole reason the gate exists. See the paragraphs on the type.
        var existingSites = await _dbContext.Sites
            .CountAsync(site => site.AccountId == command.AccountId, cancellationToken);
        if (existingSites >= account.MaxSites)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.SiteLimitReached), ErrorType.Conflict), cancellationToken);
        }

        // Deliberately ignores the tenant filter: a hostname is claimed once across the whole
        // server, so a name already served for ANOTHER account is still taken. Without this, the
        // filter would hide the conflicting row, the check would pass, and the insert would fail on
        // the key as an unhandled exception instead of a typed 409.
        //
        // The check covers the ALIASES as well as the domain, and covers them against other sites'
        // aliases as well as their domains, because nginx answers a request by Host alone: an alias
        // naming another tenant's domain takes that domain over, ACME challenge location included
        // (SiteHostname). The database key is what actually decides it — this check exists to turn
        // the collision into a typed 409 rather than a fault.
        var claimed = command.Aliases
            .Select(alias =>
            {
                return alias.ToLowerInvariant();
            })
            .Append(command.Domain.ToLowerInvariant())
            .ToList();
#pragma warning disable RS0030 // a hostname is claimed server-wide; scoping this read would let one account take another's domain
        var domainTaken = await _dbContext.SiteHostnames
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hostname => claimed.Contains(hostname.Name), cancellationToken);
#pragma warning restore RS0030
        if (domainTaken)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.SiteDomainTaken), ErrorType.Conflict), cancellationToken);
        }

        if (command.BackendType == SiteBackendType.Php)
        {
            var installed = await IsPhpVersionInstalledAsync(command.PhpVersion, cancellationToken);
            if (!installed.IsSuccess)
            {
                return await FailAsync(command, installed.Error!, cancellationToken);
            }

            if (!installed.Value)
            {
                return await FailAsync(command, Error.Of(nameof(ErrorMessages.PhpVersionNotInstalled), ErrorType.Validation), cancellationToken);
            }
        }

        var provisioned = await _agent.CreateAsync(
            account.Username,
            command.Domain,
            command.Aliases,
            SiteBackendKindOf(command.BackendType),
            command.PhpVersion,
            command.ProxyUpstream,
            // The plan budget travels with the creation because the agent writes the site's
            // php-fpm pool as part of creating it. It used to write only the vhost, and a PHP site
            // was therefore born pointing at a socket nothing had bound — it answered 502 until
            // somebody changed its version, which was the only operation that wrote a pool.
            (uint)account.MaxPhpWorkersPerPool,
            NoSettingOverrides,
            cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, provisioned.Error!, cancellationToken);
        }

        var site = new Site(
            Guid.NewGuid(),
            command.AccountId,
            command.Domain,
            command.Aliases,
            command.BackendType,
            command.PhpVersion,
            command.ProxyUpstream,
            provisioned.Value.DocumentRoot,
            _clock.UtcNow);

        if (!await _slotGate.TryTakeAsync(site, account.MaxSites, cancellationToken))
        {
            // The gate refused, which means another creation committed the account's last slot while
            // this one was on the host. Nothing owns the vhost this request made, so it goes, and the
            // refusal says the plan filled up rather than that the domain was taken — the domain is
            // not the problem and deleting a site the customer no longer needs is what unblocks them.
            await CompensateAsync(account.Username, command, cancellationToken);

            return await FailAsync(
                command,
                Error.Of(nameof(ErrorMessages.SiteLimitReachedConcurrently), ErrorType.Conflict),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.SiteCreated, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Ok(new SiteDto(
            site.Id, site.AccountId, site.Domain, site.BackendType, site.PhpVersion, site.Status, site.CreatedAt));
    }

    /// <summary>Maps the module's backend enum onto the agent client's, for a site that has no row yet.</summary>
    /// <param name="backendType">The requested backend type.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know; see <see cref="Maran.Modules.Sites.Mappers.SiteDescriptorMapper"/>.</exception>
    private static SiteBackendKind SiteBackendKindOf(SiteBackendType backendType)
    {
        return backendType switch
        {
            SiteBackendType.Static => SiteBackendKind.Static,
            SiteBackendType.Php => SiteBackendKind.Php,
            SiteBackendType.ReverseProxy => SiteBackendKind.ReverseProxy,
            _ => throw new ArgumentOutOfRangeException(nameof(backendType), backendType, "Unmapped site backend type."),
        };
    }

    /// <summary>Removes a vhost the agent provisioned but no row owns.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="command">The creation being undone; its domain addresses the delete.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <remarks>
    /// Best effort, and logged rather than surfaced: the customer is already being told the creation
    /// failed, and a second failure here changes nothing they can act on. The empty PHP version is
    /// deliberate and is argued on the type — it leaves every php-fpm pool standing, because a pool is
    /// shared by the account's other sites on that version and removing one would take them off the
    /// air, while an orphaned pool is merely wasteful.
    /// </remarks>
    private async Task CompensateAsync(
        string accountUsername,
        CreateSiteCommand command,
        CancellationToken cancellationToken)
    {
        var deleted = await _agent.DeleteAsync(
            accountUsername, command.Domain, string.Empty, cancellationToken);
        if (!deleted.IsSuccess)
        {
            LogCompensationFailed(_logger, command.Domain, deleted.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused, whose domain is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SiteDto>> FailAsync(
        CreateSiteCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.SiteCreated, command.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Fail(error);
    }

    /// <summary>Asks the agent whether a PHP version is installed on this host.</summary>
    /// <param name="version">The two-component version requested.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether it is installed, or the agent's own typed failure — which is NOT the same answer as "not installed".</returns>
    private async Task<Result<bool>> IsPhpVersionInstalledAsync(string version, CancellationToken cancellationToken)
    {
        var versions = await _php.ListVersionsAsync(cancellationToken);
        if (!versions.IsSuccess)
        {
            return Result<bool>.Fail(versions.Error!);
        }

        return Result<bool>.Ok(versions.Value.Any(installed =>
        {
            return string.Equals(installed.Version, version, StringComparison.Ordinal);
        }));
    }
}
