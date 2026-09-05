using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

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

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything about the site that lives on the host.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>The host's PHP runtimes, so a site is never bound to a version that is not installed.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the site.</param>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new site's creation time.</param>
    public CreateSiteCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        IAgentPhpClient php,
        SiteAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _php = php;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Creates the site, refusing it before the host is touched when the plan or the domain says no.</summary>
    /// <param name="command">The validated site parameters; see <see cref="CreateSiteCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The created site, or <c>AccountNotFound</c>, <c>SiteLimitReached</c>, <c>SiteDomainTaken</c>,
    /// <c>PhpVersionNotInstalled</c>, or the agent's own typed failure.
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
        // KNOWN RACE, and deliberately not solved here: this is count-then-insert with no database
        // constraint behind it, so two concurrent creates can both read N and both insert, leaving
        // the account one over its plan. The Domain unique index (SiteConfiguration) closes the
        // equivalent race for domains because a domain is a single value a UNIQUE can cover; a
        // per-account COUNT is not, and the honest fixes are a serializable transaction or a
        // per-account counter row. Being one site over a plan limit is a billing discrepancy an
        // operator can see and correct, not a tenancy or availability failure, so it is recorded
        // rather than fixed in this pass.
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

        _dbContext.Sites.Add(site);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
