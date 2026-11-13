using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Revokes the API keys a login created when that login is suspended, and journals each revocation
/// with the reason <c>CreatorDisabled</c> (issue #551): a key lives only as long as the person who
/// issued it may act, and a suspension ends that for good.
/// </summary>
/// <remarks>
/// <para>
/// <b>The authenticator is the defence in depth; this is the revocation.</b> <see cref="ApiKeyAuthenticator"/>
/// refuses a key whose creating login is absent or may not sign in, on the next call. This type makes the
/// key's own row say so (<see cref="Domain.Entities.ApiKey.RevokedAt"/>, which the key list shows as
/// revoked) and leaves an audit entry naming the reason.
/// </para>
/// <para>
/// <b>Resuming the login does not revive the keys.</b> The revocation is stored on the key, so a leaked
/// key stays dead after the resume and a new key has to be issued.
/// </para>
/// <para>
/// <b>One save covers the revocations and their entries.</b> Identity owns the journal's table, so the
/// entries are staged on the same context as the revoked rows (<see cref="IStagedAuditWriter"/>) and the
/// caller's single <c>SaveChangesAsync</c> stores the suspension, every revocation and every entry, or
/// none of them. A failed save leaves keys unrevoked and unrecorded, and the retry finds and records them
/// again; no entry can be stored for a key that stayed active, and none can be lost for a key that was
/// revoked. This type never saves.
/// </para>
/// </remarks>
public sealed class ApiKeyCreatorRevoker
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Stages the entry of each revocation on the same context.</summary>
    private readonly IStagedAuditWriter _audit;

    /// <summary>Creates the revoker.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="audit">Stages the entry of each revocation on the same context.</param>
    public ApiKeyCreatorRevoker(IdentityDbContext dbContext, IClock clock, IStagedAuditWriter audit)
    {
        _dbContext = dbContext;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Revokes, without saving, every key still unrevoked that the login created, and stages one entry for each.</summary>
    /// <param name="creatorUserId">The login about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many keys this call revoked; zero when none was left.</returns>
    public async Task<int> RevokeAsync(Guid creatorUserId, CancellationToken cancellationToken)
    {
        var keys = await _dbContext.ApiKeys
            .Where(key => key.CreatedByUserId == creatorUserId && key.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;
        foreach (var key in keys)
        {
            key.Revoke(now);
            _audit.Stage(IdentityAuditJournal.ApiKeyRevokedForCreatorEntry(key));
        }

        return keys.Count;
    }
}
