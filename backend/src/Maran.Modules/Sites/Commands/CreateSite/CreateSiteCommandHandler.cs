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
using Maran.Sdk.Events;
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
/// <b>The creation is announced last</b> (<see cref="SiteCreated"/>, through <see cref="SiteCreationAnnouncer"/>),
/// after the row and the journal entry. A subscriber, such as the Dns module setting up the site's zone,
/// cannot fail or undo a creation that already happened: its failure is logged and the site is returned.
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

    /// <summary>The settings a site that has no pool is sent: none, because there is nothing to carry them.</summary>
    private static readonly IReadOnlyList<PhpSettingDto> NoSettingOverrides = [];

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything about the site that lives on the host.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>
    /// What the host has installed: the stack gate, the web server the vhost is rendered for, and the
    /// runtimes a site may be bound to.
    /// </summary>
    private readonly SiteStackGate _stack;

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

    /// <summary>
    /// The settings the account's pools carry. The pool the agent writes belongs to an ACCOUNT and a
    /// version rather than to this site, so a creation that left them out would rewrite an existing
    /// pool without what the customer had set.
    /// </summary>
    private readonly PhpAccountPoolSettings _poolSettings;

    /// <summary>Tells the other modules the site exists once its row is committed.</summary>
    private readonly SiteCreationAnnouncer _announcer;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the site.</param>
    /// <param name="stack">Reads what is installed: the stack gate, the web server and the runtimes.</param>
    /// <param name="claims">Reads which committed sites already claim a hostname.</param>
    /// <param name="recorder">Writes the row under the atomic slot claim, removing the vhost again when it cannot.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new site's creation time.</param>
    /// <param name="siteNames">Names the site's backend and serving state for the request's culture.</param>
    /// <param name="poolSettings">The settings the account's pools carry, sent with every pool write.</param>
    /// <param name="announcer">Tells the other modules the site exists once its row is committed.</param>
    public CreateSiteCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        SiteStackGate stack,
        SiteHostnameClaims claims,
        SiteRecorder recorder,
        SiteAuditJournal journal,
        IClock clock,
        SiteDisplayNames siteNames,
        PhpAccountPoolSettings poolSettings,
        SiteCreationAnnouncer announcer)
    {
        _dbContext = dbContext;
        _siteNames = siteNames;
        _poolSettings = poolSettings;
        _accounts = accounts;
        _agent = agent;
        _stack = stack;
        _claims = claims;
        _recorder = recorder;
        _journal = journal;
        _clock = clock;
        _announcer = announcer;
    }

    /// <summary>Creates the site, refusing it before the host is touched when the plan or the domain says no.</summary>
    /// <param name="command">The validated site parameters; see <see cref="CreateSiteCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The created site, or <c>AccountNotFound</c>, <c>SiteLimitReached</c>,
    /// <c>SiteLimitReachedConcurrently</c>, <c>SiteDomainTaken</c>, <c>AppsStackNotReady</c>,
    /// <c>AppsNotInstalled</c>, <c>SiteAppPortTaken</c>, or
    /// the agent's own typed failure.
    /// </returns>
    /// <exception cref="DbUpdateException">
    /// Rethrown when the row cannot be written for any reason other than a taken hostname — a dropped
    /// connection, a timeout — after the vhost the agent made has been removed again. That is an
    /// infrastructure fault, not an answer about the request (rules/csharp.md "Errors").
    /// </exception>
    public async Task<Result<SiteDto>> HandleAsync(CreateSiteCommand command, CancellationToken cancellationToken)
    {
        // The stack gate comes before anything else is read or asked: with no site web server installed
        // there is nothing a site could be served by. Enforced here and not in the SPA, which only
        // reflects it (docs/apps/README.md).
        var installed = await _stack.ReadAsync(cancellationToken);
        if (installed.NotReadyFailure() is { } notReady)
        {
            return await FailAsync(command, notReady, cancellationToken);
        }

        // Ready means a site web server is installed, so this is never null past the gate above; the
        // vhost is rendered for the one web server the host has.
        var webServer = installed.WebServer!.Value;

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

        // A runtime is bound only if it is installed as an app. The apps are the host's record of what
        // it has, so a missing one is AppsNotInstalled and the customer is told to ask for it, rather
        // than a site being born pointing at a socket or a binary nothing provides.
        if (command.BackendType == SiteBackendType.Php && !installed.HasPhp(command.PhpVersion))
        {
            return await FailAsync(command, SiteHostApps.NotInstalledFailure(), cancellationToken);
        }

        if (command.BackendType == SiteBackendType.Node)
        {
            if (!installed.HasNode(command.NodeVersion))
            {
                return await FailAsync(command, SiteHostApps.NotInstalledFailure(), cancellationToken);
            }

            // Two applications cannot listen on one loopback port, whichever accounts own them.
            var portTaken = await _claims.IsAppPortTakenAsync(command.AppPort, cancellationToken);
            if (portTaken)
            {
                return await FailAsync(command, Error.Of(nameof(ErrorMessages.SiteAppPortTaken), ErrorType.Conflict), cancellationToken);
            }
        }

        // What a Node site runs, or nothing for any other backend: values a request carried for a
        // backend that does not use them are dropped here, so the row never holds a port or a command
        // that no unit runs.
        var node = NodeAppOf(command);

        var provisioned = await _agent.CreateAsync(
            account.Username,
            command.Domain,
            command.Aliases,
            SiteBackendKindOf(command.BackendType),
            command.PhpVersion,
            command.ProxyUpstream,
            webServer,
            node,
            // The plan budget travels with the creation because the agent writes the site's
            // php-fpm pool as part of creating it. It used to write only the vhost, and a PHP site
            // was therefore born pointing at a socket nothing had bound — it answered 502 until
            // somebody changed its version, which was the only operation that wrote a pool.
            (uint)account.MaxPhpWorkersPerPool,
            command.BackendType == SiteBackendType.Php
                ? await _poolSettings.EffectiveAsync(command.AccountId, account.PhpLimits, cancellationToken)
                : NoSettingOverrides,
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
            node.Version,
            (int)node.Port,
            node.StartCommand,
            provisioned.Value.DocumentRoot,
            _clock.UtcNow);

        var recorded = await _recorder.RecordAsync(site, account.Username, account.MaxSites, webServer, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return await FailAsync(command, recorded.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            SiteAuditActions.SiteCreated, site.Domain, command.IpAddress, command.UserAgent, cancellationToken);

        // After the row and the journal, and it cannot fail the creation: a subscriber's failure is logged
        // by the announcer, because the site exists whatever another module could not set up for it.
        await _announcer.AnnounceAsync(new SiteCreated(site.Id, site.AccountId, site.Domain), cancellationToken);

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
            site.CreatedAt,
            site.LogRetentionDays));
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
            SiteBackendType.Node => SiteBackendKind.Node,
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

    /// <summary>What the agent is told a Node site runs; nothing for any other backend.</summary>
    /// <param name="command">The validated creation.</param>
    /// <returns>The Node application, or <see cref="SiteNodeApp.None"/> for a site that has none.</returns>
    private static SiteNodeApp NodeAppOf(CreateSiteCommand command)
    {
        return command.BackendType == SiteBackendType.Node
            ? new SiteNodeApp(command.NodeVersion, (uint)command.AppPort, command.StartCommand)
            : SiteNodeApp.None;
    }
}
