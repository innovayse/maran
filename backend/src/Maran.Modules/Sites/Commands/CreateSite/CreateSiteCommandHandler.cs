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
using Npgsql;

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
/// <see cref="SiteHostname"/> describes. <see cref="RecordAsync"/> reads that refusal —
/// <c>SqlState 23505</c> and nothing else — answers <c>SiteDomainTaken</c>, and removes the loser's
/// vhost.
/// </para>
/// <para>
/// <b>Why this loser is compensated, when the Ftp and Sftp losers of the same race are not.</b> There
/// the winner and the loser made the SAME login on the host, so a delete would revoke the winner's
/// credential. A vhost is a file named after the site's PRIMARY domain, so a loser that collided on an
/// alias, or whose domain collided with another site's alias, wrote a file of its own that no row owns,
/// and it goes. The two coincide in exactly one case: both requests named the same primary domain and
/// both wrote the one file. The agent refuses a creation whose vhost is already there, under the same
/// lock, and this handler again does not rely on that: if it happens, the file now belongs to the
/// winner's row, deleting it would take the winner's site off the air, so the delete is withheld and
/// reported (<see cref="CompensateUnlessOwnedAsync"/>) — the same conservative direction the pool
/// paragraph below takes. Which of the two renderings the file then holds, this handler cannot see.
/// </para>
/// <para>
/// <b>What the compensation cannot close on its own.</b> Between the loser's reload and its
/// compensating delete the loser's vhost is live, and for those seconds it may answer for the winner's
/// name. The database side cannot close that window; the agent's refusal above is what keeps such a
/// vhost off the host in the first place. Taking the claims before the agent is called would close it
/// here too, and was not chosen: a row that exists before its vhost is a row every reader of this
/// module — the site list, suspension, deletion, the certificate directory — would have to learn to
/// skip, and a process killed between the claim and the agent leaves a claim nothing releases, which
/// needs a pending state, a migration and a reconciler of its own.
/// </para>
/// <para>
/// Nor does the compensation run on every failure after the agent, and the line is drawn by what is
/// KNOWN. A refused insert (<c>DbUpdateException</c>) proves nothing was committed, so the vhost is
/// this request's alone and goes. Anything else the gate throws — the commit failing, the connection
/// dropping around it, the request being cancelled — leaves open whether the row was written, and
/// deleting the vhost of a site whose row did commit is the reverse disagreement, a site the panel
/// shows that does not answer. So it is rethrown untouched, and a vhost with no row can survive it;
/// a process killed between the agent and the insert leaves one too. Both surface as a server
/// failure, never as a typed answer about the request. Once a compensation has been decided on, it
/// runs WITHOUT the request's cancellation: a caller who hangs up after the host work is done must
/// not abandon the removal the hang-up made necessary, and every agent call is already bounded by the
/// agent client's own operation timeout.
/// </para>
/// <para>
/// <b>The loser pays for it on the host, which is why the pre-checks stay.</b> The gate runs after the
/// agent has provisioned the site, so a request refused there leaves a vhost nothing owns, and it is
/// removed again. The compensating delete retires NO php-fpm pool — it passes the empty version, which
/// the agent documents as "leave every pool alone" — because a pool belongs to an ACCOUNT and a version
/// rather than to one site, and the account's other sites on that version share it. An orphaned pool is
/// wasteful and an operator can see it; a removed shared pool takes another of the customer's sites off
/// the air, so the conservative direction is the only defensible one for a compensation.
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

    /// <summary>Pre-compiled log delegate for a compensation withheld because the vhost is another site's.</summary>
    /// <remarks>
    /// An error and not a warning, because what it reports needs an operator: two creations of one
    /// domain both wrote its vhost, the other one's row owns it, and the file may hold THIS request's
    /// rendering — its document root and its aliases — until the owning site's vhost is rendered
    /// again. It names the domain only; never a path and never an account.
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogCompensationWithheld =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(CreateSiteCommandHandler)),
            "Provisioned site {Domain} lost its name to a concurrent creation of the same domain after "
            + "both had written its vhost; the vhost was left in place because the winning site's row "
            + "owns it, and it may carry the losing request's rendering until that site is re-rendered.");

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

    /// <summary>Where a failed or withheld compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateSiteCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that provisions the site.</param>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    /// <param name="slotGate">Takes the account's last free slot atomically when the row is written.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new site's creation time.</param>
    /// <param name="logger">Where a failed or withheld compensation is reported.</param>
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
        // insert, and RecordAsync turns a lost race into the same typed 409 and removes the vhost the
        // loser made. It exists so that the ordinary collision is refused before the host is touched.
        var claimed = command.Aliases
            .Select(alias =>
            {
                return alias.ToLowerInvariant();
            })
            .Append(command.Domain.ToLowerInvariant())
            .ToList();
        var claimants = await ClaimingSiteDomainsAsync(claimed, cancellationToken);
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

        var recorded = await RecordAsync(site, account.Username, account.MaxSites, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return await FailAsync(command, recorded.Error!, cancellationToken);
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

    /// <summary>Writes the row for a site the agent has already provisioned, removing its vhost if it cannot.</summary>
    /// <param name="site">The row to write, carrying its hostname claims.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating delete.</param>
    /// <param name="allowance">How many sites the account's plan allows, re-checked inside the gate.</param>
    /// <param name="command">The creation being recorded; its domain addresses the compensating delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with — not yet journalled.</returns>
    /// <exception cref="DbUpdateException">Rethrown, after the vhost is removed, for any failure but a taken hostname.</exception>
    private async Task<Result<bool>> RecordAsync(
        Site site,
        string accountUsername,
        int allowance,
        CreateSiteCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await _slotGate.TryTakeAsync(site, allowance, cancellationToken))
            {
                return Result<bool>.Ok(true);
            }
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The ONE narrowing, and the answer turns on it. SqlState 23505 means another creation
            // committed a claim on one of this site's hostnames — or its domain — first; nothing else
            // does. A DbUpdateException is also the one throw from the gate that proves nothing was
            // committed (ISiteSlotGate), so the vhost this request made is owned by no row of its own
            // and goes, unless the winner's row owns that very file.
            await CompensateUnlessOwnedAsync(accountUsername, command);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.SiteDomainTaken), ErrorType.Conflict));
        }
        catch (DbUpdateException)
        {
            // Any other refused insert says nothing about the request and is rethrown — reporting a
            // dropped connection as "this domain is taken" would tell the customer to change a name
            // that was never the problem. Nothing was committed here either, so the vhost goes first,
            // and unconditionally: asking whether another site owns the file is a read against the
            // database that has just failed, and a same-domain winner would have surfaced as a 23505.
            await CompensateAsync(accountUsername, command);
            throw;
        }

        // The gate refused, which means another creation committed the account's last slot while this
        // one was on the host. Nothing owns the vhost this request made, so it goes, and the refusal
        // says the plan filled up rather than that the domain was taken — the domain is not the
        // problem and deleting a site the customer no longer needs is what unblocks them.
        await CompensateUnlessOwnedAsync(accountUsername, command);

        return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.SiteLimitReachedConcurrently), ErrorType.Conflict));
    }

    /// <summary>Removes the vhost this request made, unless a committed site with the same domain owns that file.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="command">The creation being undone; its domain addresses the delete.</param>
    /// <remarks>
    /// <para>
    /// Takes no cancellation token: once the removal is decided on it must not be abandoned by a caller
    /// hanging up (see the type's remarks), and the agent client bounds the call itself.
    /// </para>
    /// <para>
    /// The agent names a vhost after the site's primary domain alone, lower-cased, and deletes by that
    /// name without asking which account wrote it. So the question before a compensating delete is
    /// not "did this request write the file" — it did — but "does a committed row now own that same
    /// file". It does exactly when this request's domain is claimed as some site's PRIMARY domain: a
    /// claim held as another site's alias names a different file, and an unclaimed domain names a
    /// file nobody owns. Only the first case withholds the delete.
    /// </para>
    /// </remarks>
    private async Task CompensateUnlessOwnedAsync(string accountUsername, CreateSiteCommand command)
    {
        var claimants = await ClaimingSiteDomainsAsync([command.Domain.ToLowerInvariant()], CancellationToken.None);
        var ownedByAnotherSite = claimants.Any(domain =>
        {
            return string.Equals(domain, command.Domain, StringComparison.OrdinalIgnoreCase);
        });
        if (ownedByAnotherSite)
        {
            LogCompensationWithheld(_logger, command.Domain, null);
            return;
        }

        await CompensateAsync(accountUsername, command);
    }

    /// <summary>Removes a vhost the agent provisioned but no row owns.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="command">The creation being undone; its domain addresses the delete.</param>
    /// <remarks>
    /// Takes no cancellation token, for the reason <see cref="CompensateUnlessOwnedAsync"/> gives.
    /// Best effort, and logged rather than surfaced: the customer is already being told the creation
    /// failed, and a second failure here changes nothing they can act on. The empty PHP version is
    /// deliberate and is argued on the type — it leaves every php-fpm pool standing, because a pool is
    /// shared by the account's other sites on that version and removing one would take them off the
    /// air, while an orphaned pool is merely wasteful.
    /// </remarks>
    private async Task CompensateAsync(string accountUsername, CreateSiteCommand command)
    {
        var deleted = await _agent.DeleteAsync(
            accountUsername, command.Domain, string.Empty, CancellationToken.None);
        if (!deleted.IsSuccess)
        {
            LogCompensationFailed(_logger, command.Domain, deleted.Error!.Code, null);
        }
    }

    /// <summary>Reads, across every account, the primary domain of each site claiming any of <paramref name="names"/>.</summary>
    /// <param name="names">Lower-cased hostnames, as <see cref="SiteHostname.Name"/> stores them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One primary domain per claimed name; empty when none of the names is claimed.</returns>
    /// <remarks>
    /// Deliberately ignores the tenant filter: a hostname is claimed once across the whole server, so
    /// a name already served for ANOTHER account is still taken, and the vhost file a compensating
    /// delete would remove may be another account's. With the filter on, the conflicting row would be
    /// hidden — the pre-check would pass a name another tenant holds, and the compensation would
    /// delete another tenant's vhost. The one read in this handler that needs the bypass, so both
    /// questions are asked through it rather than each carrying a bypass of its own.
    /// </remarks>
    private async Task<IReadOnlyList<string>> ClaimingSiteDomainsAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // a hostname is claimed server-wide; scoping this read would let one account take another's domain
        return await _dbContext.SiteHostnames
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(hostname => names.Contains(hostname.Name))
            .Select(hostname => hostname.Site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
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
