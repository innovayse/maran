using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Answers an ACME DNS-01 challenge by publishing the TXT record through the DNS module's challenge door,
/// and words the door's refusals as this module's own failures.
/// </summary>
/// <remarks>
/// <para>
/// <b>The DNS module may not be there.</b> It is a separate, optional module, and this one may not
/// reference it. The constructor takes every registered <see cref="IDnsChallengeRecords"/> (none when the
/// panel is composed without the DNS module) and not the container: asking the container at run time for
/// a window that may not exist is service location, which is a review reject. None means a wildcard cannot
/// be offered, and that is reported as an answer.
/// </para>
/// <para>
/// <b>The door's codes do not travel outward.</b> A customer reading a certificate order should be told
/// what to fix about the certificate, so a zone that is not served and a name server that is switched off
/// are this module's <c>Wildcard…</c> codes, and every other refusal is <c>AcmeDnsChallengeWriteFailed</c>.
/// An agent that cannot be reached keeps its own kind, so the answer stays a 503 and not a 500.
/// </para>
/// </remarks>
public sealed class AcmeDnsChallengeWriter : IAcmeDnsChallengeWriter
{
    /// <summary>The DNS module's code for a name that no zone of the account contains.</summary>
    private const string DnsZoneNotFoundCode = "DnsChallengeZoneNotFound";

    /// <summary>The DNS module's code for a zone that does not exist or is not the account's.</summary>
    private const string ZoneNotFoundCode = "ZoneNotFound";

    /// <summary>The DNS module's code for a name server that is switched off.</summary>
    private const string DnsNotEnabledCode = "DnsNotEnabled";

    /// <summary>The DNS module's code for a name server whose settings are not stated.</summary>
    private const string DnsSettingsIncompleteCode = "DnsSettingsIncomplete";

    /// <summary>The DNS module's challenge door, or <c>null</c> when the panel has no DNS module.</summary>
    private readonly IDnsChallengeRecords? _records;

    /// <summary>Creates the writer over whatever DNS module the panel was composed with.</summary>
    /// <param name="records">Every registered challenge door: one when the DNS module is composed, none otherwise.</param>
    public AcmeDnsChallengeWriter(IEnumerable<IDnsChallengeRecords> records)
    {
        _records = records.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<WildcardAvailability> CheckAsync(
        Guid accountId,
        string domain,
        CancellationToken cancellationToken)
    {
        if (_records is null)
        {
            return WildcardAvailability.DnsModuleAbsent;
        }

        var availability = await _records.CheckAsync(accountId, domain, cancellationToken);
        return availability switch
        {
            DnsChallengeAvailability.Available => WildcardAvailability.Available,
            DnsChallengeAvailability.DnsNotEnabled => WildcardAvailability.DnsNotEnabled,
            _ => WildcardAvailability.ZoneNotServed,
        };
    }

    /// <inheritdoc />
    public async Task<Result<DnsChallengeHandle>> PublishAsync(
        Guid accountId,
        string domain,
        string txtValue,
        CancellationToken cancellationToken)
    {
        if (_records is null)
        {
            return Result<DnsChallengeHandle>.Fail(
                Error.Of(nameof(ErrorMessages.WildcardDnsNotAvailable), ErrorType.Conflict));
        }

        var published = await _records.PublishAsync(accountId, domain, txtValue, cancellationToken);
        return published.IsSuccess
            ? published
            : Result<DnsChallengeHandle>.Fail(Word(published.Error!));
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(DnsChallengeHandle handle, CancellationToken cancellationToken)
    {
        if (_records is null)
        {
            return Result.Ok();
        }

        return await _records.RemoveAsync(handle, cancellationToken);
    }

    /// <summary>Words the door's refusal as this module's failure.</summary>
    /// <param name="refusal">The refusal the DNS module answered with.</param>
    /// <returns>The error to answer with, code and kind together.</returns>
    private static Error Word(Error refusal)
    {
        if (refusal.Code is DnsZoneNotFoundCode or ZoneNotFoundCode)
        {
            return Error.Of(nameof(ErrorMessages.WildcardZoneNotServed), ErrorType.Conflict);
        }

        if (refusal.Code is DnsNotEnabledCode or DnsSettingsIncompleteCode)
        {
            return Error.Of(nameof(ErrorMessages.WildcardDnsNotEnabled), ErrorType.Conflict);
        }

        return refusal.Type == ErrorType.Unavailable
            ? refusal
            : Error.Of(nameof(ErrorMessages.AcmeDnsChallengeWriteFailed), ErrorType.Failure);
    }
}
