using System.Net;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Decides whether the value presented in the <c>X-Api-Key</c> header is a working key, for the
/// Host's authentication handler (issue #144).
/// </summary>
/// <remarks>
/// <para>
/// <b>One answer, equal work.</b> A header that cannot be a key by SHAPE is refused before any lookup
/// or hashing, which discloses nothing because the shape is public. Every value of the right shape is
/// then treated alike: the row is looked up by its public prefix, the SHA-256 digest of the presented
/// value is computed, and it is compared in constant time against the stored digest, or against a
/// fixed digest of the same length when no row has that prefix. Only after the comparison are the
/// state (revoked, expired) and the address binding read, and a refusal for ANY reason, an unknown
/// prefix included, is the same <see cref="ApiKeyDenied"/> with no reason attached. The reason is
/// written to the operator's log with the key's id or prefix and the caller's address, never the
/// presented value. A database hit against a miss is not provably equal in time; what that could
/// leak is whether a 32-bit public index value exists, which names no secret (threat note, section 2).
/// </para>
/// <para>
/// <b>No cache.</b> The row is read on every request, so a revocation takes effect on the next call. A
/// key is also refused while the login that created it does not exist or may not authenticate
/// (suspended, not yet active, or deleted), judged after the digest comparison and for a matching key
/// only, so it adds no difference between an unknown prefix and a known one. That is the root of the
/// rule for every key, an administrator's and a reseller's alike (issue #551): a credential a person
/// issued does not outlive the person's right to sign in. This check is defence in depth: a reseller's
/// suspension also revokes its keys on the row (<see cref="ApiKeyCreatorRevoker"/>), so resuming the
/// login does not revive them, and a missing creator row refuses its keys regardless.
/// </para>
/// <para>
/// <b>Last use</b> is recorded only after a successful validation, so wrong keys cannot move a real
/// key's last use, through the entity's own throttle (at most one write a minute per key), and the
/// save happens only when the entity says it changed. A failed write is logged and never fails the
/// request, because a bookkeeping field must not be able to deny a valid credential.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticator : IApiKeyAuthenticator
{
    /// <summary>
    /// The digest compared against when no row has the presented prefix: the digest of an empty string,
    /// which has the length of every stored digest and which no key of the checked shape can equal.
    /// </summary>
    private static readonly string UnknownPrefixDigest = ApiKeyHasher.Digest(string.Empty);

    /// <summary>
    /// Pre-compiled log delegate for a header that is not shaped like a key; it carries the caller's
    /// address and never the presented value.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogMalformedHeader =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(ApiKeyAuthenticator)),
            "API key refused: the header is not shaped like a key (address {Address}).");

    /// <summary>
    /// Pre-compiled log delegate for a key of the right shape that matches no stored digest; it carries
    /// the public prefix and the caller's address, never the presented value.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LogNoMatch =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2, nameof(ApiKeyAuthenticator)),
            "API key refused: no key matches (prefix {Prefix}, address {Address}).");

    /// <summary>
    /// Pre-compiled log delegate for a matching key that is not usable or not allowed from the caller's
    /// address; it carries the key id and the caller's address, never the presented value.
    /// </summary>
    private static readonly Action<ILogger, Guid, bool, bool, string, Exception?> LogRefused =
        LoggerMessage.Define<Guid, bool, bool, string>(
            LogLevel.Information,
            new EventId(3, nameof(ApiKeyAuthenticator)),
            "API key {KeyId} refused: usable {Usable}, address allowed {AddressAllowed} (address {Address}).");

    /// <summary>
    /// Pre-compiled log delegate for a key whose creating login may not sign in (suspended, not yet
    /// active, or gone); it carries the key id and the caller's address, never the presented value.
    /// </summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogCreatorInactive =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(5, nameof(ApiKeyAuthenticator)),
            "API key {KeyId} refused: the login that created it may not authenticate (address {Address}).");

    /// <summary>
    /// Pre-compiled log delegate for a last-use stamp that could not be saved; the exception is attached
    /// and the call proceeds.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception?> LogLastUseNotRecorded =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(4, nameof(ApiKeyAuthenticator)),
            "API key {KeyId}: the last use could not be recorded; the call proceeds.");

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Where refusals are recorded for the operator.</summary>
    private readonly ILogger<ApiKeyAuthenticator> _logger;

    /// <summary>Creates the authenticator.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="logger">Where refusals are recorded for the operator.</param>
    public ApiKeyAuthenticator(IdentityDbContext dbContext, ILogger<ApiKeyAuthenticator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ApiKeyAuthentication> AuthenticateAsync(
        string headerValue,
        string clientAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!ApiKeyHasher.TryParse(headerValue, out var prefix))
        {
            LogMalformedHeader(_logger, clientAddress, null);
            return ApiKeyDenied.Instance;
        }

        var key = await _dbContext.ApiKeys.SingleOrDefaultAsync(existing => existing.Prefix == prefix, cancellationToken);

        // The digest is computed and compared whether or not a row was found, so an unknown prefix costs
        // what a known one does; nothing below reads the row's state before this comparison has run.
        var digestMatches = ApiKeyHasher.DigestsMatch(ApiKeyHasher.Digest(headerValue), key?.TokenHash ?? UnknownPrefixDigest);

        if (key is null || !digestMatches)
        {
            LogNoMatch(_logger, prefix, clientAddress, null);
            return ApiKeyDenied.Instance;
        }

        var usable = key.IsUsable(now);
        var addressAllowed = IsAddressAllowed(key, clientAddress);

        if (!usable || !addressAllowed)
        {
            LogRefused(_logger, key.Id, usable, addressAllowed, clientAddress, null);
            return ApiKeyDenied.Instance;
        }

        if (!await CreatorMayAuthenticateAsync(key.CreatedByUserId, cancellationToken))
        {
            LogCreatorInactive(_logger, key.Id, clientAddress, null);
            return ApiKeyDenied.Instance;
        }

        await RecordUseAsync(key, now, clientAddress, cancellationToken);

        return new ApiKeyAuthenticated(key.Id, ApiKeyScopePolicy.EffectiveOf(key), key.ResellerId);
    }

    /// <summary>
    /// Whether the login that created a key still exists and may authenticate. A key lives only as long
    /// as the person who issued it may sign in: suspending a reseller suspends its login, and a login
    /// that was deleted, or never accepted its invitation, cannot have a working key. Read on every call,
    /// with no cache, so a suspension takes effect on the next request.
    /// </summary>
    /// <param name="creatorUserId">The login that issued the key (<see cref="ApiKey.CreatedByUserId"/>).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when the creating login exists and may sign in.</returns>
    private async Task<bool> CreatorMayAuthenticateAsync(Guid creatorUserId, CancellationToken cancellationToken)
    {
        var login = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == creatorUserId, cancellationToken);

        return login is { MayAuthenticate: true };
    }

    /// <summary>
    /// Whether the caller's address may use the key. A key with no ranges is not bound; a bound key is
    /// refused for an address that cannot be read, which includes the unknown peer.
    /// </summary>
    /// <param name="key">The key row.</param>
    /// <param name="clientAddress">The caller's address as <see cref="ClientAddress"/> spells it.</param>
    /// <returns>True when the key is unbound, or the address lies inside one of its ranges.</returns>
    private static bool IsAddressAllowed(ApiKey key, string clientAddress)
    {
        if (key.AllowedCidrs is null)
        {
            return true;
        }

        if (!IPAddress.TryParse(clientAddress, out var address))
        {
            return false;
        }

        return key.AllowedCidrs.Any(text =>
        {
            return CidrRule.TryParse(text, out var rule) && rule!.Contains(address);
        });
    }

    /// <summary>Stamps the key's last use, when the entity's throttle says a write is due.</summary>
    /// <param name="key">The tracked key row that just authenticated a call.</param>
    /// <param name="now">The instant of the call.</param>
    /// <param name="clientAddress">The caller's address.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the write was made, skipped or failed; it never throws for a failed write.</returns>
    private async Task RecordUseAsync(ApiKey key, DateTimeOffset now, string clientAddress, CancellationToken cancellationToken)
    {
        if (!key.RecordUse(now, clientAddress))
        {
            return;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The unsaved stamp must not ride along on a later save in this request.
            _dbContext.Entry(key).State = EntityState.Detached;
            LogLastUseNotRecorded(_logger, key.Id, exception);
        }
    }
}
