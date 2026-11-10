using Maran.Modules.Ssl.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Interfaces;

/// <summary>
/// Places and removes the TXT record that answers an ACME DNS-01 challenge, in a zone this server serves
/// for the account. The provider hook of wildcard issuance: Maran's own name server is the one provider.
/// </summary>
/// <remarks>
/// A separate seam from <see cref="IAcmeChallengeWriter"/> because it talks to something else: that one
/// writes FILES through the agent, this one writes a RECORD through the DNS module. The implementation
/// reaches the DNS module only through the Sdk's <see cref="Maran.Sdk.Interfaces.IDnsChallengeRecords"/>
/// window, which may be absent when the panel is composed without that module; absence is an answer
/// (<see cref="WildcardAvailability.DnsModuleAbsent"/>), never an exception.
/// </remarks>
public interface IAcmeDnsChallengeWriter
{
    /// <summary>Tells whether a domain's DNS-01 challenge can be answered here, without writing anything.</summary>
    /// <param name="accountId">The account whose zones are searched.</param>
    /// <param name="domain">The domain being validated.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// <see cref="WildcardAvailability.Available"/>, <see cref="WildcardAvailability.DnsModuleAbsent"/>,
    /// <see cref="WildcardAvailability.DnsNotEnabled"/> or <see cref="WildcardAvailability.ZoneNotServed"/>;
    /// the plan is not asked here.
    /// </returns>
    Task<WildcardAvailability> CheckAsync(Guid accountId, string domain, CancellationToken cancellationToken);

    /// <summary>Publishes one challenge value at <c>_acme-challenge.&lt;domain&gt;</c>.</summary>
    /// <param name="accountId">The account whose zone receives the record.</param>
    /// <param name="domain">The domain being validated.</param>
    /// <param name="txtValue">The record's value: the base64url SHA-256 of the key authorization.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The receipt to remove the record with, or this module's failure: <c>WildcardZoneNotServed</c>,
    /// <c>WildcardDnsNotEnabled</c>, <c>WildcardDnsNotAvailable</c>, the agent's own unavailability as itself,
    /// or <c>AcmeDnsChallengeWriteFailed</c> for every other refusal.
    /// </returns>
    Task<Result<DnsChallengeHandle>> PublishAsync(
        Guid accountId,
        string domain,
        string txtValue,
        CancellationToken cancellationToken);

    /// <summary>Removes a record that <see cref="PublishAsync"/> published.</summary>
    /// <param name="handle">The receipt.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, including for a record that is already gone, or the DNS module's typed failure.</returns>
    /// <remarks>
    /// Best-effort by contract, like the file writer's: the caller MUST NOT fail an otherwise-successful
    /// issuance because the cleanup failed. A stale challenge record proves nothing to anybody once its
    /// authorization is consumed, and the DNS module sweeps one that outlives its hour.
    /// </remarks>
    Task<Result> RemoveAsync(DnsChallengeHandle handle, CancellationToken cancellationToken);
}
