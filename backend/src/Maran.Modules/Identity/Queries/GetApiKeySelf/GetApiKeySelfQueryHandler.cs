using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Queries.GetApiKeySelf;

/// <summary>Handles <see cref="GetApiKeySelfQuery"/> by reading the calling key's own row.</summary>
public sealed class GetApiKeySelfQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetApiKeySelfQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the key's name, prefix, scopes, expiry and last use.</summary>
    /// <param name="query">Which key is asking, from its authenticated principal.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key's own view, or <c>ApiKeyNotFound</c> when the row was deleted since the call authenticated.</returns>
    public async Task<Result<ApiKeySelfDto>> HandleAsync(GetApiKeySelfQuery query, CancellationToken cancellationToken)
    {
        var key = await _dbContext.ApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(existing => existing.Id == query.ApiKeyId, cancellationToken);

        if (key is null)
        {
            return Result<ApiKeySelfDto>.Fail(Error.Of(nameof(ErrorMessages.ApiKeyNotFound), ErrorType.NotFound));
        }

        return Result<ApiKeySelfDto>.Ok(ApiKeyMapper.Self(key));
    }
}
