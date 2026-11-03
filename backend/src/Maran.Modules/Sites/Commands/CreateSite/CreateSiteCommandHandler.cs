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

namespace Maran.Modules.Sites.Commands.CreateSite;

/// <summary>
/// Handles <see cref="CreateSiteCommand"/>: refuses what the plan does not allow, provisions the
/// site on the host through the agent, and only then records the row that defines it (spec §11) —
/// removing the vhost again on every path where that row is known not to have been written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: plan and names first, agent second, row third.</b> The plan limit and the hostnames are
/// checked before the agent is called at all: a site the panel refuses must never reach the host, or
/// the panel has created something it will then have to remember to remove. The agent runs before
/// the row because the two stores can disagree either way and this order decides WHICH way. Neither
/// way is harmless. A vhost with no row is invisible to the panel and still answers on the host for
/// every name it lists, while those names are claimed by nothing in the database — so another site
/// can claim one of them, and whichever file sorts first in the include glob answers for it
/// (<see cref="SiteHostname"/>). Nor does a retry repair it: the agent reports a domain it already
/// has a vhost for as <c>AlreadyExists</c> and rewrites nothing. That is why every path below on
/// which the agent succeeded and the row was not written removes the vhost again. The reverse order —
/// a row the panel shows as a live site with no vhost behind it — is a customer told they have a site
/// that does not answer, and has no such repair.
/// </para>
/// <para>
/// <b>The limit is checked twice, and the SECOND check is the one that holds.</b> The pre-agent check
/// is count-then-insert with nothing behind the count, so on its own two concurrent creations both read
/// N-1 and both succeed, leaving the account one site over its plan. It is kept because it refuses the
/// ordinary over-limit request before the host is touched at all. What closes the window is
/// <see cref="ISiteSlotGate"/>: the row is written under a per-account advisory lock, with the count
/// re-taken inside it, so the second of two simultaneous creations sees the first one's committed row
/// and is refused. The gate's own documentation says what each rejected alternative — a unique index,
/// an insert carrying the count, a serializable transaction, a counter column — is blind to, and why the
/// agent contributes nothing here.
/// </para>
/// <para>
/// <b>The hostnames are checked three times, and the last two decide — one for the host, one for the
/// database.</b> The pre-check below reads committed rows only, so two creations whose pre-checks both
/// ran before either committed both pass it. The agent is the check that protects the HOST: it
/// introduces a vhost under its config-tree lock and refuses one naming a host another vhost already
/// serves (<c>ops::sites::create_vhost</c>), so of two such creations the second is refused there with
/// no vhost written, and <see cref="TranslateProvisioningFailure"/> answers it <c>SiteDomainTaken</c>.
/// The key on <c>SiteHostnames</c> decides for the DATABASE: a hostname is a value, so the key refuses
/// the second claim at the insert and two rows can never own one name. This handler does not lean on
/// the agent's refusal to keep the two stores consistent — it is another process's answer, read from
/// nginx's own wording — so a creation that got past the agent and then loses at the key is handled
/// here as well: its vhost is live, naming a hostname another row now owns, which is the takeover
/// <see cref="SiteHostname"/> describes. <see cref="SiteRecorder"/> reads that refusal —
/// <c>SqlState 23505</c> and nothing else — answers <c>SiteDomainTaken</c>, and removes the loser's
/// vhost.
/// </para>
/// <para>
/// <b>Which failures after the agent are compensated</b>, and why a loser's vhost goes while a pool never
/// does, are argued on <see cref="SiteRecorder"/> and <see cref="SiteVhostCompensator"/>.
/// </para>
/// <para>
/// Every refusal is journalled as well as every success: a plan limit hit, a taken domain, an
/// account the caller may not see and an agent that said no are exactly the events an operator
/// later needs to explain what happened (<see cref="AuditEntry"/>).
/// </para>
/// </remarks>
public sealed class CreateSiteCommandHandler
{
    /// <summary>The agent client's stable code for "the host already has a vhost for this domain".</summary>
    /// <remarks>
    /// The code string rather than <see cref="ErrorType.Conflict"/>, even though the agent client maps
    /// that one wire code and one other onto that kind. The kind is a property of the whole translator
    /// and covers a full plan too; the code names the one refusal this handler is entitled to re-read
    /// as a taken name. The same shape the Ftp, Backups and Firewall modules use to recognise one agent
    /// code.
    /// </remarks>
    private const string AgentAlreadyExistsCode = "AgentAlreadyExists";

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

    /// <summary>Writes the row under the atomic slot claim, removing the vhost again when it cannot.</summary>
    private readonly SiteRecorder _recorder;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Reads which committed sites already claim a hostname, across every account.</summary>
    private readonly SiteHostnameClaims _claims;

    /// <summary>Names the site's backend and serving state in the request's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the site.</param>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    /// <param name="claims">Reads which committed sites already claim a hostname.</param>
    /// <param name="recorder">Writes the row under the atomic slot claim, removing the vhost again when it cannot.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new site's creation time.</param>
    /// <param name="siteNames">Names the site's backend and serving state for the request's culture.</param>
    public CreateSiteCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        IAgentPhpClient php,
        SiteHostnameClaims claims,
        SiteRecorder recorder,
        SiteAuditJournal journal,
        IClock clock,
        SiteDisplayNames siteNames)
    {
        _dbContext = dbContext;
        _siteNames = siteNames;
        _accounts = accounts;
        _agent = agent;
        _php = php;
        _claims = claims;
        _recorder = recorder;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Creates the site, refusing it before the host is touched when the plan or the domain says no.</summary>
    /// <param name="command">The validated site parameters; see <see cref="CreateSiteCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The created site, or <c>AccountNotFound</c>, <c>SiteLimitReached</c>,
    /// <c>SiteLimitReachedConcurrently</c>, <c>SiteDomainTaken</c>, <c>PhpVersionNotInstalled</c>, or
    /// the agent's own typed failure.
    /// </returns>
    /// <exception cref="DbUpdateException">
    /// Rethrown when the row cannot be written for any reason other than a taken hostname — a dropped
    /// connection, a timeout — after the vhost the agent made has been removed again. That is an
    /// infrastructure fault, not an answer about the request (rules/csharp.md "Errors").
    /// </exception>
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
        // row is written. A per-account COUNT is not a value a key can cover, which is the whole
        // reason the gate exists. See the paragraphs on the type.
        var existingSites = await _dbContext.Sites
            .CountAsync(site => site.AccountId == command.AccountId, cancellationToken);
        if (existingSites >= account.MaxSites)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.SiteLimitReached), ErrorType.Conflict), cancellationToken);
        }

        // The check covers the ALIASES as well as the domain, and covers them against other sites'
        // aliases as well as their domains, because nginx answers a request by Host alone: an alias
        // naming another tenant's domain takes that domain over, ACME challenge location included
        // (SiteHostname). It is not what decides a race — the key on SiteHostnames does, at the
        // insert, and SiteRecorder turns a lost race into the same typed 409 and removes the vhost the
        // loser made. It exists so that the ordinary collision is refused before the host is touched.
        var claimed = command.Aliases
            .Select(alias =>
            {
                return alias.ToLowerInvariant();
            })
            .Append(command.Domain.ToLowerInvariant())
            .ToList();
        var claimants = await _claims.ClaimingSiteDomainsAsync(claimed, cancellationToken);
        if (claimants.Count > 0)
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
            return await FailAsync(command, TranslateProvisioningFailure(provisioned.Error!), cancellationToken);
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

        var recorded = await _recorder.RecordAsync(site, account.Username, account.MaxSites, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return await FailAsync(command, recorded.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            SiteAuditActions.SiteCreated, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteDto>.Ok(new SiteDto(
            site.Id,
            site.AccountId,
            account.Username,
            site.Domain,
            site.BackendType,
            _siteNames.Of(site.BackendType),
            site.PhpVersion,
            site.Status,
            _siteNames.Of(site.Status),
            site.CreatedAt));
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

    /// <summary>Re-reads the agent's refusal as a sentence about the NAME where that is what it means.</summary>
    /// <param name="error">The typed failure the agent client produced.</param>
    /// <returns>The domain-taken failure for a host-side collision; otherwise <paramref name="error"/> unchanged.</returns>
    /// <remarks>
    /// <para>
    /// The pre-check above reads only committed rows, so a creation of the same domain that is still
    /// on the host, or a vhost no row owns, passes it — and the agent then refuses the creation as
    /// <c>AlreadyExists</c> without writing a vhost (its document root may already exist). Left
    /// untranslated, the customer reads a sentence
    /// about the agent for a condition whose whole content is "this domain is taken"; so the code
    /// becomes <c>SiteDomainTaken</c>, and the KIND stays <see cref="ErrorType.Conflict"/>, which is
    /// what the agent's refusal already meant.
    /// </para>
    /// <para>
    /// Only this one code is re-read. Every other agent failure keeps its own code and kind, because
    /// a name the customer could change is the only thing this translation can honestly claim. Nothing
    /// is compensated on this path: the agent refused, so this request put no vhost on the host.
    /// </para>
    /// </remarks>
    private static Error TranslateProvisioningFailure(Error error)
    {
        if (!string.Equals(error.Code, AgentAlreadyExistsCode, StringComparison.Ordinal))
        {
            return error;
        }

        return Error.Of(nameof(ErrorMessages.SiteDomainTaken), ErrorType.Conflict);
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
            SiteAuditActions.SiteCreated, command.Domain, command.IpAddress, command.UserAgent, cancellationToken);

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
