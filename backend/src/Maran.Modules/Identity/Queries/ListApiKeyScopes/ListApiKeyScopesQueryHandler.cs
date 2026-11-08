using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Queries.ListApiKeyScopes;

/// <summary>
/// Handles <see cref="ListApiKeyScopesQuery"/> by naming every scope of the Sdk's closed vocabulary for
/// the request's culture.
/// </summary>
public sealed class ListApiKeyScopesQueryHandler
{
    /// <summary>Names each scope as an administrator reads it, in the request's culture.</summary>
    private readonly ApiKeyScopeDisplayNames _scopeNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="scopeNames">Names an API key scope for the request's culture.</param>
    public ListApiKeyScopesQueryHandler(ApiKeyScopeDisplayNames scopeNames)
    {
        _scopeNames = scopeNames;
    }

    /// <summary>Returns every scope the asking owner can grant a key, named, in the vocabulary's order.</summary>
    /// <param name="query">The request, carrying the reseller asking or none for the administrator.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; the vocabulary is in code, so this never fails.</returns>
    public Task<Result<IReadOnlyList<ApiKeyChoiceDto>>> HandleAsync(
        ListApiKeyScopesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApiKeyChoiceDto> choices = ApiKeyScopes.GrantableTo(query.OwnerResellerId)
            .Select(scope =>
            {
                return new ApiKeyChoiceDto(scope, _scopeNames.Of(scope));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<ApiKeyChoiceDto>>.Ok(choices));
    }
}
