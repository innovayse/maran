using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// The DNS module's answer to <see cref="IDnsChallengeRecords"/>: publishes and removes the one TXT record a
/// DNS-01 validation needs, in a zone the named account owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a thin layer over the zone.</b> What a challenge record is — its name, its digest-shaped value,
/// the two-values-per-name bound, the exemption from the record cap — is decided by
/// <see cref="ChallengeRecordPolicy"/> and <see cref="Zone.AddChallengeRecord"/>. This type finds the zone,
/// opens the edit through <see cref="DnsZoneEditor"/> (the same lock, the same agent-first order as every
/// other change of a zone) and restates the zone's verdict as a typed failure.
/// </para>
/// <para>
/// <b>Tenancy is the account argument.</b> The caller may be unattended, so the tenant filter shows it
/// nothing; the zone is looked for among the named account's zones only, on a line that says so. A name
/// inside another account's zone is therefore a name with no zone, which is also how a customer's own query
/// would answer.
/// </para>
/// <para>
/// <b>The zone's lock is taken inside <see cref="DnsZoneEditor"/> and released by its commit</b>, so it is
/// held for one write and never across the authority's validation, which happens between the two calls.
/// </para>
/// </remarks>
public sealed class DnsChallengeRecords : IDnsChallengeRecords
{
    /// <summary>The module's context; the zone is looked for through it with the tenant filter lifted for one account.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Opens and publishes edits of one zone.</summary>
    private readonly DnsZoneEditor _editor;

    /// <summary>The clock the record's creation instant and the zone's serial are taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seam over this module's zones.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="editor">Opens and publishes edits of one zone.</param>
    /// <param name="clock">The clock.</param>
    public DnsChallengeRecords(DnsDbContext dbContext, DnsZoneEditor editor, IClock clock)
    {
        _dbContext = dbContext;
        _editor = editor;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<DnsChallengeAvailability> CheckAsync(
        Guid accountId,
        string hostname,
        CancellationToken cancellationToken)
    {
        if (!ChallengeRecordPolicy.IsHostname(hostname))
        {
            return DnsChallengeAvailability.NoZone;
        }

        if (await FindZoneAsync(accountId, hostname, cancellationToken) is null)
        {
            return DnsChallengeAvailability.NoZone;
        }

        var settings = await _dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);

        return settings is { Enabled: true } && settings.IsComplete
            ? DnsChallengeAvailability.Available
            : DnsChallengeAvailability.DnsNotEnabled;
    }

    /// <inheritdoc />
    public async Task<Result<DnsChallengeHandle>> PublishAsync(
        Guid accountId,
        string hostname,
        string txtValue,
        CancellationToken cancellationToken)
    {
        if (!ChallengeRecordPolicy.IsValue(txtValue))
        {
            return Result<DnsChallengeHandle>.Fail(
                Error.Of(nameof(ErrorMessages.DnsChallengeValueInvalid), ErrorType.Validation));
        }

        if (!ChallengeRecordPolicy.IsHostname(hostname))
        {
            return Result<DnsChallengeHandle>.Fail(
                Error.Of(nameof(ErrorMessages.DnsChallengeHostnameInvalid), ErrorType.Validation));
        }

        var found = await FindZoneAsync(accountId, hostname, cancellationToken);
        if (found is null)
        {
            return Result<DnsChallengeHandle>.Fail(
                Error.Of(nameof(ErrorMessages.DnsChallengeZoneNotFound), ErrorType.NotFound));
        }

        var opened = await _editor.OpenForAccountAsync(accountId, found.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            return Result<DnsChallengeHandle>.Fail(opened.Error!);
        }

        await using var edit = opened.Value;
        var draft = new RecordDraft(
            ChallengeRecordPolicy.RecordNameIn(edit.Zone.Name, hostname),
            RecordType.Txt,
            DnsLimits.MinTtl,
            txtValue);

        var recordId = Guid.NewGuid();
        var verdict = edit.Zone.AddChallengeRecord(recordId, draft, _clock.UtcNow);
        if (verdict != RecordRuleVerdict.Accepted)
        {
            return Result<DnsChallengeHandle>.Fail(RecordRuleErrors.From(verdict));
        }

        var published = await _editor.PublishAsync(edit, cancellationToken);
        if (!published.IsSuccess)
        {
            return Result<DnsChallengeHandle>.Fail(published.Error!);
        }

        return Result<DnsChallengeHandle>.Ok(new DnsChallengeHandle(accountId, edit.Zone.Id, recordId));
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(DnsChallengeHandle handle, CancellationToken cancellationToken)
    {
        var opened = await _editor.OpenForAccountAsync(handle.AccountId, handle.ZoneId, cancellationToken);
        if (!opened.IsSuccess)
        {
            // A zone that is gone took its records with it, which is exactly what was asked for.
            return opened.Error!.Type == ErrorType.NotFound ? Result.Ok() : Result.Fail(opened.Error);
        }

        await using var edit = opened.Value;
        var verdict = edit.Zone.RemoveChallengeRecord(handle.RecordId, _clock.UtcNow);
        if (verdict == RecordRuleVerdict.RecordNotFound)
        {
            return Result.Ok();
        }

        if (verdict != RecordRuleVerdict.Accepted)
        {
            return Result.Fail(RecordRuleErrors.From(verdict));
        }

        return await _editor.PublishAsync(edit, cancellationToken);
    }

    /// <summary>Finds the most specific zone of an account that contains a hostname.</summary>
    /// <param name="accountId">The account whose zones are searched, and no other's.</param>
    /// <param name="hostname">A name accepted by <see cref="ChallengeRecordPolicy.IsHostname"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The zone's identity and apex, or <c>null</c> when none of the account's zones contains the name.</returns>
    private async Task<FoundZone?> FindZoneAsync(Guid accountId, string hostname, CancellationToken cancellationToken)
    {
        var candidates = ChallengeRecordPolicy.ZoneCandidates(hostname).ToArray();

#pragma warning disable RS0030 // no principal for an unattended caller: the account the caller names scopes the read instead
        var zones = await _dbContext.Zones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(zone => zone.AccountId == accountId && candidates.Contains(zone.Name))
            .Select(zone => new FoundZone(zone.Id, zone.Name))
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        return zones.OrderByDescending(zone =>
        {
            return zone.Name.Length;
        }).FirstOrDefault();
    }

    /// <summary>A zone as the search found it: enough to open an edit of it.</summary>
    /// <param name="Id">The zone's identity.</param>
    /// <param name="Name">The zone's apex.</param>
    private sealed record FoundZone(Guid Id, string Name);
}
