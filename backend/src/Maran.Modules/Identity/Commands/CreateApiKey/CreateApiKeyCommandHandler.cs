using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Commands.CreateApiKey;

/// <summary>
/// Handles <see cref="CreateApiKeyCommand"/> by minting a key, storing only its digest and returning
/// the plaintext once.
/// </summary>
/// <remarks>
/// <para>
/// <b>The plaintext lives in this method and in the returned DTO, and nowhere else.</b> The row holds
/// the SHA-256 digest; the audit entry holds the prefix, the name and the scopes. The result travels
/// through <c>IMessageBus.InvokeAsync</c> in memory, never a durable queue, so no Wolverine envelope
/// ever holds it.
/// </para>
/// <para>
/// The ceiling counts keys that still work, per owner (the administrator's against the panel's ceiling, a
/// reseller's against its own): a revoked or expired key frees its place, so
/// the limit bounds the standing credentials and not the history. A minted prefix that already exists
/// (a one in four billion draw) is discarded and minted again, and the collision is never surfaced
/// as a prefix; if every redraw collides the creation fails with <c>ApiKeyPrefixUnavailable</c>
/// instead of falling through to the unique index.
/// </para>
/// <para>
/// <b>The count, the collision check and the insert are one serialised step.</b> They run inside the
/// transaction <see cref="IApiKeyCreationLock"/> opens, which holds a panel-wide advisory lock until
/// the commit, so two simultaneous creations cannot both count one below the limit and both insert.
/// The journal entry is written after the commit: a lost entry is preferable to a journalled key that
/// was rolled back.
/// </para>
/// </remarks>
public sealed class CreateApiKeyCommandHandler
{
    /// <summary>How many times a colliding prefix is redrawn before the mint is given up on.</summary>
    private const int MaxMintAttempts = 5;

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the creation.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Serialises key creation across the panel.</summary>
    private readonly IApiKeyCreationLock _creationLock;

    /// <summary>Names the new key's status in the request's culture.</summary>
    private readonly ApiKeyStatusDisplayNames _statusNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the creation.</param>
    /// <param name="creationLock">Serialises key creation across the panel.</param>
    /// <param name="statusNames">Names an API key status for the request's culture.</param>
    public CreateApiKeyCommandHandler(
        IdentityDbContext dbContext,
        IClock clock,
        IdentityAuditJournal journal,
        IApiKeyCreationLock creationLock,
        ApiKeyStatusDisplayNames statusNames)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
        _creationLock = creationLock;
        _statusNames = statusNames;
    }

    /// <summary>Creates the key.</summary>
    /// <param name="command">The validated key parameters, the login creating it and the reseller that will own it, if any.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The key and its plaintext, or <c>ApiKeyExpiryInvalid</c> for an expiry that is not in the
    /// future, <c>ApiKeyLimitReached</c> when the owner already holds its maximum of working keys, or
    /// <c>ApiKeyPrefixUnavailable</c> when every drawn prefix collided.
    /// </returns>
    public async Task<Result<ApiKeyCreatedDto>> HandleAsync(
        CreateApiKeyCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (command.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return Result<ApiKeyCreatedDto>.Fail(Error.Of(nameof(ErrorMessages.ApiKeyExpiryInvalid), ErrorType.Validation));
        }

        if (!TryNormalizeCidrs(command.AllowedCidrs, out var allowedCidrs))
        {
            return Result<ApiKeyCreatedDto>.Fail(Error.Of(nameof(ErrorMessages.ApiKeyCidrInvalid), ErrorType.Validation));
        }

        ApiKey key;
        string plaintext;
        await using (var transaction = await _creationLock.BeginAsync(cancellationToken))
        {
            // Counted per owner: the administrator's keys against the panel's ceiling, one reseller's
            // keys against that reseller's own, so neither can fill the other's room.
            var working = await _dbContext.ApiKeys
                .Where(existing => existing.ResellerId == command.OwnerResellerId)
                .CountAsync(existing => existing.RevokedAt == null && (existing.ExpiresAt == null || existing.ExpiresAt > now), cancellationToken);

            var ceiling = command.OwnerResellerId is null ? ApiKeyLimits.MaxKeys : ApiKeyLimits.MaxKeysPerReseller;
            if (working >= ceiling)
            {
                return Result<ApiKeyCreatedDto>.Fail(Error.Of(nameof(ErrorMessages.ApiKeyLimitReached), ErrorType.Conflict));
            }

            var (minted, prefix, digest) = ApiKeyHasher.Mint();
            var attempts = 1;
            while (await _dbContext.ApiKeys.AnyAsync(existing => existing.Prefix == prefix, cancellationToken))
            {
                if (attempts >= MaxMintAttempts)
                {
                    return Result<ApiKeyCreatedDto>.Fail(Error.Of(nameof(ErrorMessages.ApiKeyPrefixUnavailable), ErrorType.Failure));
                }

                (minted, prefix, digest) = ApiKeyHasher.Mint();
                attempts++;
            }

            plaintext = minted;
            key = ApiKey.Issue(
                Guid.NewGuid(),
                command.Name,
                prefix,
                digest,
                command.Scopes.Distinct(StringComparer.Ordinal).ToList(),
                allowedCidrs,
                command.CreatedByUserId,
                now,
                command.ExpiresAt,
                command.OwnerResellerId);

            _dbContext.ApiKeys.Add(key);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await _journal.RecordApiKeyAsync(
            command.CreatedByUserId,
            IdentityAuditActions.ApiKeyCreated,
            key,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<ApiKeyCreatedDto>.Ok(ApiKeyMapper.Created(key, plaintext, now, _statusNames));
    }

    /// <summary>
    /// Reads each range into its canonical text, so the same range written two ways is stored once;
    /// <c>null</c> is a key not bound to any address (the validator refuses an empty list).
    /// </summary>
    /// <param name="cidrs">The ranges as the caller sent them.</param>
    /// <param name="normalized">The distinct canonical ranges, or <c>null</c> when the key is not address-bound.</param>
    /// <returns>
    /// <c>false</c> when a range cannot be read, which the validator makes unreachable through the API
    /// but a command dispatched on the bus without it could still produce.
    /// </returns>
    private static bool TryNormalizeCidrs(IReadOnlyList<string>? cidrs, out List<string>? normalized)
    {
        normalized = null;

        if (cidrs is null)
        {
            return true;
        }

        var texts = new List<string>(cidrs.Count);

        foreach (var text in cidrs)
        {
            if (!CidrRule.TryParse(text, out var rule))
            {
                return false;
            }

            texts.Add(rule!.ToString());
        }

        normalized = texts.Distinct(StringComparer.Ordinal).ToList();
        return true;
    }
}
