using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Dns.IntegrationEvents.Handlers;

/// <summary>
/// Creates the DNS zone of a site that was just created (<see cref="SiteCreated"/>), when this server is
/// publishing DNS, the account's plan sells zones and nothing already covers the site's name.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asks for the zone through <see cref="CreateZoneCommand"/> and decides nothing about who may have
/// it.</b> Who may create which name, the overlap with other accounts' zones, the plan allowance under its
/// lock and the host-side order are all the command handler's, so an automatic zone and a hand-made one can
/// never be held to different rules. What this handler adds is only the list of reasons NOT to ask, so an
/// ordinary site creation on a host that does not serve DNS leaves no refused-creation entry in the audit
/// journal for every site: the name server is off or its settings are unstated, the account's plan sells no
/// zone or the allowance is spent, the name is not a zone apex, or the account already owns the name or a
/// zone above it. None of those is a failure and none is logged above information; a refusal by the command itself is a warning.
/// </para>
/// <para>
/// <b>A zone below one the account already owns is not created.</b> The daemon answers from the most
/// specific zone it holds, so a new <c>blog.example.com</c> zone beside the account's own <c>example.com</c>
/// would take that subtree out of the parent zone and stop answering for the parent's records of it. The
/// site's name is already inside a zone the customer controls, and that is where its records belong.
/// </para>
/// <para>
/// <b>The zone is the site's primary domain, as it is stored.</b> Nothing here knows where the registrable
/// domain ends (the module carries no public-suffix list), so a site on <c>shop.example.com</c> gets the zone
/// <c>shop.example.com</c>; a customer who wants the zone above it creates that one by hand.
/// </para>
/// <para>
/// <b>The zone is made from the default template when the administrator chose one</b>
/// (<see cref="DnsSettings.DefaultTemplateId"/>). A template that cannot be applied, because it was removed from
/// the database, can no longer expand into valid records or holds more than a zone may, never costs the site
/// its zone: the empty zone is created instead and the failure is logged as a warning.
/// </para>
/// <para>
/// <b>A site's deletion does not remove the zone</b>, so there is no counterpart to this handler: mail and
/// other records may live in it. It goes with the account (<see cref="AccountDeletingHandler"/>) or by the
/// customer's or administrator's own deletion.
/// </para>
/// <para>
/// <b>It runs inline as the caller who created the site</b>, so the account is read through that caller's
/// tenant scope and the zone creation is judged as that caller. It never throws for a refusal: the site
/// exists whatever happens here, and the zone can be created from the DNS screen.
/// </para>
/// </remarks>
public sealed class SiteCreatedHandler
{
    /// <summary>Pre-compiled log delegate for an account the caller can no longer read.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAccountMissing =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1, nameof(SiteCreatedHandler)),
            "No zone was created for the new site of account {AccountId}: the account is not readable");

    /// <summary>Pre-compiled log delegate for a name that is not a zone apex.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogNameNotAZone =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(2, nameof(SiteCreatedHandler)),
            "No zone was created for site {SiteId}: its primary domain is not a name a zone can have");

    /// <summary>Pre-compiled log delegate for an account whose allowance is already spent.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAllowanceSpent =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(3, nameof(SiteCreatedHandler)),
            "No zone was created for site {SiteId}: the account has used its plan's zones");

    /// <summary>Pre-compiled log delegate for a zone the creation command refused.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogRefused =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(4, nameof(SiteCreatedHandler)),
            "The zone of site {SiteId} was not created: {Code}");

    /// <summary>Pre-compiled log delegate for a default template that could not be applied.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogTemplateFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(5, nameof(SiteCreatedHandler)),
            "The default zone template could not be applied to site {SiteId} ({Code}); an empty zone is created instead");

    /// <summary>The Dns module's database context, read for the settings and the account's own zones.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The window onto the Accounts module's rows: the plan's zone allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The bus the zone creation is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a skipped or refused creation is recorded.</summary>
    private readonly ILogger<SiteCreatedHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module.</param>
    /// <param name="bus">The bus the zone creation is invoked on.</param>
    /// <param name="logger">Where a skipped or refused creation is recorded.</param>
    public SiteCreatedHandler(
        DnsDbContext dbContext,
        IAccountDirectory accounts,
        IMessageBus bus,
        ILogger<SiteCreatedHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>Creates the zone of the new site when there is a reason to and none not to.</summary>
    /// <param name="message">The site that was created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the zone was created or it was decided not to.</returns>
    public async Task HandleAsync(SiteCreated message, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        if (settings is null || !settings.Enabled || !settings.IsComplete)
        {
            return;
        }

        var account = await _accounts.FindAsync(message.AccountId, cancellationToken);
        if (account is null)
        {
            LogAccountMissing(_logger, message.AccountId, null);
            return;
        }

        if (account.MaxDnsZones <= 0)
        {
            return;
        }

        var name = ZoneNamePolicy.Normalize(message.Domain);
        if (ZoneNamePolicy.Check(name) != ZoneNameVerdict.Accepted)
        {
            LogNameNotAZone(_logger, message.SiteId, null);
            return;
        }

        // The explicit account predicate matters: an administrator's context sees every account's zones.
        var owned = await _dbContext.Zones
            .AsNoTracking()
            .Where(zone => zone.AccountId == account.Id)
            .Select(zone => zone.Name)
            .ToListAsync(cancellationToken);
        if (owned.Any(existing =>
        {
            return name == existing || name.EndsWith("." + existing, StringComparison.Ordinal);
        }))
        {
            return;
        }

        if (owned.Count >= account.MaxDnsZones)
        {
            LogAllowanceSpent(_logger, message.SiteId, null);
            return;
        }

        var created = await _bus.InvokeAsync<Result<ZoneDto>>(
            new CreateZoneCommand(account.Id, name, TemplateId: settings.DefaultTemplateId), cancellationToken);
        if (!created.IsSuccess && settings.DefaultTemplateId is not null && IsTemplateFailure(created.Error!.Code))
        {
            LogTemplateFailed(_logger, message.SiteId, created.Error.Code, null);
            created = await _bus.InvokeAsync<Result<ZoneDto>>(
                new CreateZoneCommand(account.Id, name), cancellationToken);
        }

        if (!created.IsSuccess)
        {
            LogRefused(_logger, message.SiteId, created.Error!.Code, null);
        }
    }

    /// <summary>Tells whether a refused creation was the template's fault and not the zone's.</summary>
    /// <param name="code">The refusal's error code.</param>
    /// <returns>True for a template that is missing, invalid or larger than a zone may be.</returns>
    private static bool IsTemplateFailure(string code)
    {
        return code is nameof(ErrorMessages.ZoneTemplateNotFound)
            or nameof(ErrorMessages.ZoneTemplateInvalid)
            or nameof(ErrorMessages.ZoneTemplateTooManyRecords);
    }
}
