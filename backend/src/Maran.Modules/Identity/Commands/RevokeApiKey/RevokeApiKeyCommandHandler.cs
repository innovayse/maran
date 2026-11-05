using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.RevokeApiKey;

/// <summary>Handles <see cref="RevokeApiKeyCommand"/> by revoking the key's row.</summary>
/// <remarks>
/// The authenticator reads the row on every request with no cache between, so the revocation takes
/// effect on the next call. Revoking is idempotent: a key already revoked answers success and writes no
/// second audit entry, so a retried request neither fails nor muddies the journal.
/// </remarks>
public sealed class RevokeApiKeyCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the revocation.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the revocation.</param>
    public RevokeApiKeyCommandHandler(IdentityDbContext dbContext, IClock clock, IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Revokes the key.</summary>
    /// <param name="command">Which key, and the administrator revoking it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success, or <c>ApiKeyNotFound</c> when no key has that id.</returns>
    public async Task<Result> HandleAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken)
    {
        var key = await _dbContext.ApiKeys.SingleOrDefaultAsync(existing => existing.Id == command.ApiKeyId, cancellationToken);

        if (key is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.ApiKeyNotFound), ErrorType.NotFound));
        }

        if (key.RevokedAt is not null)
        {
            return Result.Ok();
        }

        key.Revoke(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordApiKeyAsync(
            command.AdministratorUserId,
            IdentityAuditActions.ApiKeyRevoked,
            key,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }
}
