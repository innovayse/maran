using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Commands.ReplaceSiteOptions;

/// <summary>
/// Handles <see cref="ReplaceSiteOptionsCommand"/>: replaces a site's per-site options, has the agent
/// render them into the vhost, and only then stores them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Agent first, row second</b>, like every other operation on a site: the row records what the agent
/// did, so a vhost the web server refused never leaves a row claiming it is live. The agent is asked
/// through the enable rpc, which renders an enabled site's vhost from the descriptor and is idempotent;
/// it is the one rpc that re-renders a vhost without touching a pool or a version.
/// </para>
/// <para>
/// <b>A disabled site is not sent to the agent.</b> A suspended site serves a stub that carries no
/// options (the agent renders none into it), so there is nothing to write, and sending an enable would
/// switch the site back on. The options are stored, and the vhost takes them when the site is enabled,
/// from the same row.
/// </para>
/// <para>
/// <b>If the row cannot be written after the agent accepted the options</b>, the vhost is rendered
/// again from the row as it was, so the host does not serve options the panel does not hold. Failing
/// to do even that is logged, because the customer is already being told the change failed.
/// </para>
/// <para>
/// <b>Passwords.</b> The plain password exists in this method only: it is hashed here and never
/// stored, logged, journalled or returned, and the agent is handed the hash. A user already on the site
/// who states no password keeps the hash the site holds.
/// </para>
/// </remarks>
public sealed class ReplaceSiteOptionsCommandHandler
{
    /// <summary>Pre-compiled log delegate for a vhost that could not be rendered again after a failed save.</summary>
    /// <remarks>
    /// An error and not a warning: the host may now serve options the panel does not hold until the
    /// site's vhost is rendered again, which an operator has to do. It names the domain and the agent's
    /// error CODE, never the agent's text, a path or any option value.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(ReplaceSiteOptionsCommandHandler)),
            "Site {Domain} options could not be saved and its vhost could not be rendered again "
            + "({AgentErrorCode}); the host may serve options the panel does not hold.");

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the vhost and the credential file.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Labels the closed sets in the request's culture, for the answer.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Where a compensation that did not take is reported.</summary>
    private readonly ILogger<ReplaceSiteOptionsCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that renders the vhost.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="siteNames">Labels the closed sets for the request's culture.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public ReplaceSiteOptionsCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        SiteAuditJournal journal,
        SiteDisplayNames siteNames,
        ILogger<ReplaceSiteOptionsCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _siteNames = siteNames;
        _logger = logger;
    }

    /// <summary>Replaces the site's options.</summary>
    /// <param name="command">Which site, and the complete new set.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The options in their new state, or <c>SiteNotFound</c>, <c>AccountNotFound</c>,
    /// <c>SiteOptionNotForBackend</c>, <c>SiteCanonicalHostNotServed</c>, <c>SiteBasicAuthPasswordRequired</c>, or the agent's own typed
    /// failure.
    /// </returns>
    /// <exception cref="DbUpdateException">
    /// Rethrown when the row cannot be written, after the vhost has been rendered again from the row as
    /// it was. An infrastructure fault, not an answer about the request (rules/csharp.md "Errors").
    /// </exception>
    public async Task<Result<SiteOptionsDto>> HandleAsync(
        ReplaceSiteOptionsCommand command,
        CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites.SingleOrDefaultAsync(candidate => candidate.Id == command.SiteId, cancellationToken);
        if (site is null)
        {
            // The subject is the identifier the caller supplied, because no domain is known — a probe
            // for a site the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(command, command.SiteId.ToString(), Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound), cancellationToken);
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        // The agent refuses an upload size on a static site and cache headers on a reverse-proxied one
        // (InvalidOptions): neither backend has the part of the vhost the option is written into. Refused
        // here with a code of its own so the customer is told which option, before the host is asked.
        if ((command.ClientMaxBodySizeMib is not null && site.BackendType == SiteBackendType.Static)
            || (command.StaticCacheDays is not null && site.BackendType == SiteBackendType.ReverseProxy))
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.SiteOptionNotForBackend), ErrorType.Validation), cancellationToken);
        }

        if (!CanonicalHostIsServed(site, command.CanonicalHost))
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.SiteCanonicalHostNotServed), ErrorType.Validation), cancellationToken);
        }

        var before = SiteOptionsValuesMapper.From(site.Options);
        var users = UsersFor(command, before);
        if (users is null)
        {
            return await FailAsync(command, site.Domain, Error.Of(nameof(ErrorMessages.SiteBasicAuthPasswordRequired), ErrorType.Validation), cancellationToken);
        }

        var after = new SiteOptionsValues(
            command.CanonicalHost,
            command.ClientMaxBodySizeMib,
            command.StaticCacheDays,
            command.Redirects
                .Select(redirect =>
                {
                    return new SiteRedirectValue(redirect.Path, redirect.Match, redirect.Target, redirect.Status);
                })
                .ToList(),
            users,
            command.IpAccessMode,
            command.IpRules
                .Select(NormalisedRule)
                .ToList(),
            command.RequestRate is { } rate ? new SiteRequestRate(rate.PerSecond, rate.Burst) : null);

        site.ReplaceOptions(after);

        // Only an enabled site has a vhost that serves the options; see the type's remarks.
        if (site.Status == SiteStatus.Enabled)
        {
            var rendered = await _agent.EnableAsync(
                account.Username, site.Domain, SiteDescriptorMapper.From(site), cancellationToken);
            if (!rendered.IsSuccess)
            {
                // The change was only ever made in memory; clearing it makes sure nothing later in
                // this request can save a set the host refused.
                _dbContext.ChangeTracker.Clear();
                return await FailAsync(command, site.Domain, rendered.Error!, cancellationToken);
            }
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await RestoreVhostAsync(site, account.Username);
            throw;
        }

        await _journal.RecordOptionsChangedAsync(
            site.Domain,
            SiteOptionGroups.Changed(before, after),
            after.BasicAuthUsers
                .Select(user =>
                {
                    return user.Name;
                })
                .ToList(),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<SiteOptionsDto>.Ok(SiteOptionsMapper.From(site, _siteNames));
    }

    /// <summary>Whether the site claims both names of the pair a canonical-host choice redirects between.</summary>
    /// <param name="site">The site, with its domain and aliases.</param>
    /// <param name="canonicalHost">The choice being made.</param>
    /// <returns>True for "none", or when the site claims both its plain name and the "www." form of it.</returns>
    /// <remarks>
    /// A canonical redirect sends the other name of the pair to the chosen one, so a site that does not
    /// answer for both would redirect a visitor from, or to, a name that is not its own.
    /// </remarks>
    private static bool CanonicalHostIsServed(Site site, CanonicalHost canonicalHost)
    {
        if (canonicalHost == CanonicalHost.None)
        {
            return true;
        }

        var claimed = site.Aliases
            .Append(site.Domain)
            .Select(name =>
            {
                return name.ToLowerInvariant();
            })
            .ToHashSet(StringComparer.Ordinal);
        var plain = site.Domain.ToLowerInvariant();
        plain = plain.StartsWith("www.", StringComparison.Ordinal) ? plain["www.".Length..] : plain;

        return claimed.Contains(plain) && claimed.Contains("www." + plain);
    }

    /// <summary>Builds the users the site will have: a stated password is hashed, an omitted one keeps the stored hash.</summary>
    /// <param name="command">The replacement.</param>
    /// <param name="before">The options as stored.</param>
    /// <returns>The users with their hashes, or null when a user with no password has none stored.</returns>
    private static List<SiteBasicAuthUserValue>? UsersFor(ReplaceSiteOptionsCommand command, SiteOptionsValues before)
    {
        var users = new List<SiteBasicAuthUserValue>(command.BasicAuthUsers.Count);
        foreach (var user in command.BasicAuthUsers)
        {
            if (!string.IsNullOrEmpty(user.Password))
            {
                users.Add(new SiteBasicAuthUserValue(user.Name, BasicAuthPasswordHash.Hash(user.Password)));
                continue;
            }

            var stored = before.BasicAuthUsers.FirstOrDefault(candidate =>
            {
                return string.Equals(candidate.Name, user.Name, StringComparison.Ordinal);
            });
            if (stored is null)
            {
                return null;
            }

            users.Add(stored);
        }

        return users;
    }

    /// <summary>Spells an IP rule the validator has accepted in its normal form <c>address/prefix</c>.</summary>
    /// <param name="rule">A rule <see cref="CidrRule.TryParse"/> accepted.</param>
    /// <returns>The normal form.</returns>
    /// <exception cref="InvalidOperationException">The rule does not parse, which the caller's own check rules out.</exception>
    private static string NormalisedRule(string rule)
    {
        if (!CidrRule.TryParse(rule, out var parsed))
        {
            throw new InvalidOperationException("A rule that was already accepted no longer parses.");
        }

        return parsed!.ToString();
    }

    /// <summary>Renders the vhost again from the row as it was stored, after the new options could not be saved.</summary>
    /// <param name="site">The site, whose tracked state still holds the unsaved replacement.</param>
    /// <param name="accountUsername">The owning account's system user name.</param>
    private async Task RestoreVhostAsync(Site site, string accountUsername)
    {
        if (site.Status != SiteStatus.Enabled)
        {
            return;
        }

        // Discarding the unsaved replacement and reading the site again is how "the row as it was"
        // is obtained; the descriptor is built from that read like every other.
        var siteId = site.Id;
        var domain = site.Domain;
        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.Sites.SingleOrDefaultAsync(candidate => candidate.Id == siteId, CancellationToken.None);
        if (stored is null)
        {
            return;
        }

        var restored = await _agent.EnableAsync(
            accountUsername, domain, SiteDescriptorMapper.From(stored), CancellationToken.None);
        if (!restored.IsSuccess)
        {
            LogRestoreFailed(_logger, domain, restored.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused replacement and returns it as the typed failure.</summary>
    /// <param name="command">The replacement that was refused.</param>
    /// <param name="subject">The site's domain, or the supplied identifier when no site was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SiteOptionsDto>> FailAsync(
        ReplaceSiteOptionsCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            SiteAuditActions.SiteOptionsChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<SiteOptionsDto>.Fail(error);
    }
}
