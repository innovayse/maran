using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The narrowest door into the DNS module: publishes and removes the one TXT record a DNS-01 validation
/// needs, in a zone the account owns, and can write nothing else. Implemented by the DNS module; consumed by
/// the certificate module, which does not reference it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it can write.</b> A single TXT record at <c>_acme-challenge.&lt;hostname&gt;</c>, with the origin
/// <c>AcmeChallenge</c> and the shortest time to live, whose value is exactly forty-three base64url
/// characters (a SHA-256 digest, unpadded). A SPF line, a DKIM key or any other text is refused by that
/// shape, so the seam cannot be used to publish anything but a challenge. At most two values may sit at one
/// name: a wildcard order proves the name and its wildcard at the same label.
/// </para>
/// <para>
/// <b>It holds no credential and decides no tenancy for the caller.</b> Every call names the account, and
/// the zone is found among that account's zones only, so a name inside another account's zone answers as a
/// name with no zone. The caller may be unattended (certificate renewal has no signed-in user), which is why
/// the account is an argument and not read from the request.
/// </para>
/// <para>
/// <b>The zone's lock is held for the write only.</b> Publishing and removing are separate calls, so the
/// lock is free while the authority validates; a validation that held it would let one slow authority stall
/// every edit of the zone.
/// </para>
/// </remarks>
public interface IDnsChallengeRecords
{
    /// <summary>Tells whether a hostname's challenge can be answered here, without writing anything.</summary>
    /// <param name="accountId">The account asking.</param>
    /// <param name="hostname">The name being validated, such as <c>example.com</c>; the record sits under <c>_acme-challenge</c> of it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The answer; a name that is not a plain lower-case host name is reported as having no zone.</returns>
    Task<DnsChallengeAvailability> CheckAsync(Guid accountId, string hostname, CancellationToken cancellationToken);

    /// <summary>Publishes the TXT record that answers a DNS-01 challenge for a hostname.</summary>
    /// <param name="accountId">The account whose zone receives the record.</param>
    /// <param name="hostname">The name being validated, without a wildcard label.</param>
    /// <param name="txtValue">The record's value: forty-three base64url characters.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The receipt, or a typed failure: the value or the name is malformed, no zone of the account contains the
    /// name, the name server is not in service, two values already sit at the name, or the agent refused the
    /// zone. Nothing is changed on a failure.
    /// </returns>
    Task<Result<DnsChallengeHandle>> PublishAsync(
        Guid accountId,
        string hostname,
        string txtValue,
        CancellationToken cancellationToken);

    /// <summary>Removes a challenge record that <see cref="PublishAsync"/> published.</summary>
    /// <param name="handle">The receipt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// Success, including when the record or its zone is already gone; a failure when the record is not a
    /// challenge record (nothing else is ever removed through this door) or the agent refused the zone.
    /// </returns>
    Task<Result> RemoveAsync(DnsChallengeHandle handle, CancellationToken cancellationToken);
}
