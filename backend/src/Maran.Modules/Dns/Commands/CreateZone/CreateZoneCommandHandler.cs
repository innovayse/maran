using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Dns.Commands.CreateZone;

/// <summary>
/// Handles <see cref="CreateZoneCommand"/>: checks who may create which name, hands the zone, empty or made from
/// its template, to the name server, and only then records it against the account's allowance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which names a caller may create is decided here, before anything reaches the host.</b> The account is
/// read through the tenant-scoped directory, so a guessed account id is "not found". A customer's name must
/// be claimed by one of that account's own sites; the claim is already unique across the server, which
/// removes first-come-first-served from the registrar delegation. Every caller is refused a name that equals,
/// contains or sits inside a zone another account owns, because the daemon answers from the most specific
/// zone it holds and an overlap would let one account answer for the other's names. The refusal for the
/// overlap names the kind of collision, which the caller can already infer from holding a claimed domain.
/// </para>
/// <para>
/// <b>The agent runs before the row, and a refused allowance takes the zone back.</b> The plan limit is
/// counted first so a zone the plan refuses never reaches the host. The count and the insert are one step
/// under a lock (<see cref="IDnsZoneSlotGate"/>), and if another creation took the last slot while this one
/// was on the host, the zone is removed from the daemon again: nothing is left serving a name the panel has
/// no row for.
/// </para>
/// <para>
/// <b>The name is locked from before the host is touched until the row is committed</b>
/// (<see cref="IDnsZoneLock.TakeForNameAsync"/>). Between those two moments the daemon serves a name the
/// database does not yet know, which is exactly what the start-up pass (<c>DnsZoneReconciler</c>) removes as
/// an orphan; that pass takes the same lock before it looks again, so it waits for this creation to finish and
/// then finds the row. The slot gate joins the transaction the lock lives in, and nothing is written to the
/// journal until it is gone, because a refusal's entry must not be rolled back with the creation.
/// </para>
/// </remarks>
public sealed class CreateZoneCommandHandler
{
    /// <summary>The Dns module's database context, and this module's tenant boundary.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The window onto the Accounts module's rows: the account, its allowance and its name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The window onto the Sites module's claims.</summary>
    private readonly ISiteHostnameDirectory _sites;

    /// <summary>Tells which names the panel itself uses.</summary>
    private readonly IReservedHostDirectory _reservedHosts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Counts and inserts under the account's lock.</summary>
    private readonly IDnsZoneSlotGate _slotGate;

    /// <summary>Keeps the start-up pass from removing the zone between its reaching the host and its row.</summary>
    private readonly IDnsZoneLock _zoneLock;

    /// <summary>Reads the other accounts' colliding zone names past the tenant filter.</summary>
    private readonly ZoneNameDirectory _zoneNames;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The authenticated principal, which decides whether a site must claim the name.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The clock the zone's first serial is taken from.</summary>
    private readonly IClock _clock;

    /// <summary>The operator's settings: how many records a zone may hold.</summary>
    private readonly IOptions<DnsOptions> _options;

    /// <summary>The viewer's-language lookup of the module's display names, for the skipped records' reasons.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module.</param>
    /// <param name="sites">The window onto the Sites module's claims.</param>
    /// <param name="reservedHosts">Tells which names the panel itself uses.</param>
    /// <param name="agent">The agent client that serves the zone.</param>
    /// <param name="slotGate">Counts and inserts under the account's lock.</param>
    /// <param name="zoneLock">Holds the zone name's lock from the host to the committed row.</param>
    /// <param name="zoneNames">Reads the other accounts' colliding zone names.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="currentUser">The authenticated principal.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The operator's settings.</param>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    public CreateZoneCommandHandler(
        DnsDbContext dbContext,
        IAccountDirectory accounts,
        ISiteHostnameDirectory sites,
        IReservedHostDirectory reservedHosts,
        IAgentDnsClient agent,
        IDnsZoneSlotGate slotGate,
        IDnsZoneLock zoneLock,
        ZoneNameDirectory zoneNames,
        DnsAuditJournal journal,
        ICurrentUser currentUser,
        IClock clock,
        IOptions<DnsOptions> options,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _sites = sites;
        _reservedHosts = reservedHosts;
        _agent = agent;
        _slotGate = slotGate;
        _zoneLock = zoneLock;
        _zoneNames = zoneNames;
        _journal = journal;
        _currentUser = currentUser;
        _clock = clock;
        _options = options;
        _names = names;
    }

    /// <summary>Creates the zone.</summary>
    /// <param name="command">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created zone, or the typed reason it was refused.</returns>
    public async Task<Result<ZoneDto>> HandleAsync(CreateZoneCommand command, CancellationToken cancellationToken)
    {
        var name = ZoneNamePolicy.Normalize(command.Name);

        // Tenant-scoped: the directory answers null for an account this caller does not own, so a guessed
        // account id reads as "not found" rather than "forbidden".
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, name, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var settings = await _dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        var refusal = await FindRefusalAsync(account, settings, name, cancellationToken);
        if (refusal is not null)
        {
            return await FailAsync(command, name, refusal, cancellationToken);
        }

        var zone = new Zone(Guid.NewGuid(), account.Id, name, _clock.UtcNow);
        var applied = await ApplyTemplateAsync(zone, command.TemplateId, settings!, cancellationToken);
        if (!applied.IsSuccess)
        {
            return await FailAsync(command, name, applied.Error!, cancellationToken);
        }

        var recorded = await ServeAndRecordAsync(zone, settings!, account.MaxDnsZones, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return await FailAsync(command, name, recorded.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsZoneCreated, name, command.IpAddress, command.UserAgent, cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([zone.AccountId], cancellationToken);

        return Result<ZoneDto>.Ok(ZoneMapper.From(
            zone, zone.Records.Count, ownerNames, settings!.Nameservers, applied.Value));
    }

    /// <summary>Makes the zone's records from the template the caller named, if any.</summary>
    /// <param name="zone">The new zone, not yet served or stored.</param>
    /// <param name="templateId">The template, or null for an empty zone.</param>
    /// <param name="settings">The settings the variables are read from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The records left out for want of a setting (empty when no template was named), or
    /// <c>ZoneTemplateNotFound</c>, <c>ZoneTemplateInvalid</c> or <c>ZoneTemplateTooManyRecords</c>.
    /// </returns>
    private async Task<Result<IReadOnlyList<SkippedTemplateRecordDto>>> ApplyTemplateAsync(
        Zone zone,
        Guid? templateId,
        DnsSettings settings,
        CancellationToken cancellationToken)
    {
        if (templateId is not { } id)
        {
            return Result<IReadOnlyList<SkippedTemplateRecordDto>>.Ok([]);
        }

        var template = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .Include(row => row.Records)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (template is null)
        {
            return Fail(nameof(ErrorMessages.ZoneTemplateNotFound), ErrorType.NotFound);
        }

        var records = template.Records.Select(record =>
        {
            return record.ToDraft();
        }).ToList();
        if (!TemplateExpansionPolicy.TryExpand(records, TemplateContext.For(zone.Name, settings), out var expansion))
        {
            return Fail(nameof(ErrorMessages.ZoneTemplateInvalid), ErrorType.Conflict);
        }

        // Re-validated against the real zone: the sample the template was saved against is not this zone.
        var wellFormed = expansion!.Drafts.All(draft =>
        {
            return RecordShapePolicy.Check(RecordShapePolicy.Normalize(draft), zone.Name) == RecordShapeVerdict.Accepted;
        });
        if (!wellFormed)
        {
            return Fail(nameof(ErrorMessages.ZoneTemplateInvalid), ErrorType.Conflict);
        }

        var verdict = zone.AddTemplateRecords(expansion.Drafts, _options.Value.MaxRecordsPerZone, _clock.UtcNow);
        if (verdict == RecordRuleVerdict.ZoneFull)
        {
            return Fail(nameof(ErrorMessages.ZoneTemplateTooManyRecords), ErrorType.Conflict);
        }

        if (verdict != RecordRuleVerdict.Accepted)
        {
            return Fail(nameof(ErrorMessages.ZoneTemplateInvalid), ErrorType.Conflict);
        }

        IReadOnlyList<SkippedTemplateRecordDto> skipped = expansion.Skipped.Select(item =>
        {
            return SkippedTemplateRecordMapper.From(item, _names);
        }).ToList();

        return Result<IReadOnlyList<SkippedTemplateRecordDto>>.Ok(skipped);
    }

    /// <summary>Builds the failed result of <see cref="ApplyTemplateAsync"/>.</summary>
    /// <param name="code">The error code.</param>
    /// <param name="type">The error kind.</param>
    /// <returns>The failed result.</returns>
    private static Result<IReadOnlyList<SkippedTemplateRecordDto>> Fail(string code, ErrorType type)
    {
        return Result<IReadOnlyList<SkippedTemplateRecordDto>>.Fail(Error.Of(code, type));
    }

    /// <summary>Weighs the request against everything that can be known before the host is touched.</summary>
    /// <param name="account">The owning account.</param>
    /// <param name="settings">The settings row, or null when none was ever saved.</param>
    /// <param name="name">The normalised apex.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The first refusal, or null when the zone may be created.</returns>
    private async Task<Error?> FindRefusalAsync(
        AccountSnapshot account,
        DnsSettings? settings,
        string name,
        CancellationToken cancellationToken)
    {
        if (settings is null || !settings.Enabled)
        {
            return Error.Of(nameof(ErrorMessages.DnsNotEnabled), ErrorType.Conflict);
        }

        if (!settings.IsComplete)
        {
            return Error.Of(nameof(ErrorMessages.DnsSettingsIncomplete), ErrorType.Conflict);
        }

        if (_reservedHosts.IsPanelHostName(name))
        {
            return Error.Of(nameof(ErrorMessages.ZoneNameReserved), ErrorType.Validation);
        }

        var isAdministrator = _currentUser.Caller is AdministratorCaller;
        if (!isAdministrator && !await _sites.AccountClaimsHostnameAsync(account.Id, name, cancellationToken))
        {
            return Error.Of(nameof(ErrorMessages.ZoneApexNotClaimed), ErrorType.Validation);
        }

        // Asked of the caller's own rows through the tenant filter (an administrator's are everyone's), so
        // a name the caller already holds is a plain conflict.
        if (await _dbContext.Zones.AsNoTracking().AnyAsync(zone => zone.Name == name, cancellationToken))
        {
            return Error.Of(nameof(ErrorMessages.ZoneNameTaken), ErrorType.Conflict);
        }

        var colliding = await _zoneNames.CollidingNamesOfOtherAccountsAsync(account.Id, name, cancellationToken);
        var overlap = ZoneOverlapPolicy.Check(name, colliding);
        if (overlap == ZoneOverlapVerdict.SameAsForeignZone)
        {
            return Error.Of(nameof(ErrorMessages.ZoneNameTaken), ErrorType.Conflict);
        }

        if (overlap != ZoneOverlapVerdict.Clear)
        {
            return Error.Of(nameof(ErrorMessages.ZoneOverlapsForeignZone), ErrorType.Conflict);
        }

        var existing = await _dbContext.Zones
            .AsNoTracking()
            .CountAsync(zone => zone.AccountId == account.Id, cancellationToken);

        return existing >= account.MaxDnsZones
            ? Error.Of(nameof(ErrorMessages.ZoneLimitReached), ErrorType.Conflict)
            : null;
    }

    /// <summary>Puts the zone on the host and records it, holding the name's lock from the first to the commit.</summary>
    /// <param name="zone">The zone to create.</param>
    /// <param name="settings">The settings the zone is published with.</param>
    /// <param name="allowance">The plan's allowance.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or the reason the zone was not served or not recorded.</returns>
    private async Task<Result> ServeAndRecordAsync(
        Zone zone,
        DnsSettings settings,
        int allowance,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _zoneLock.TakeForNameAsync(zone.Name, cancellationToken);

        var served = await _agent.PutZoneAsync(ZoneDefinitionMapper.From(zone, settings), cancellationToken);
        if (!served.IsSuccess)
        {
            return Result.Fail(served.Error!);
        }

        var recorded = await RecordAsync(zone, allowance, cancellationToken);
        if (recorded.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return recorded;
    }

    /// <summary>Takes the zone's slot, and takes the zone back from the host when the allowance was spent meanwhile.</summary>
    /// <param name="zone">The zone the daemon is already serving.</param>
    /// <param name="allowance">The plan's allowance.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or the reason the zone was not recorded.</returns>
    private async Task<Result> RecordAsync(Zone zone, int allowance, CancellationToken cancellationToken)
    {
        try
        {
            if (await _slotGate.TryTakeAsync(zone, allowance, cancellationToken))
            {
                return Result.Ok();
            }

            await _agent.DeleteZoneAsync(zone.Name, cancellationToken);

            return Result.Fail(Error.Of(nameof(ErrorMessages.ZoneLimitReachedConcurrently), ErrorType.Conflict));
        }
        catch (DbUpdateException)
        {
            // The unique index on the name refused: another creation of the same name won the race. The zone
            // is NOT taken back from the daemon, and that is deliberate: both zones are empty and rendered
            // from the same settings, so what the daemon serves is already the winner's, and removing it
            // would take the winner's zone off the host while its row exists.
            return Result.Fail(Error.Of(nameof(ErrorMessages.ZoneNameTaken), ErrorType.Conflict));
        }
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">The zone name the caller supplied, normalised.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ZoneDto>> FailAsync(
        CreateZoneCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.DnsZoneCreated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ZoneDto>.Fail(error);
    }
}
