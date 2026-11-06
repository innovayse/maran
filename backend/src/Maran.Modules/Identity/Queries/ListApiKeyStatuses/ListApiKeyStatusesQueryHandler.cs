using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListApiKeys;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.ListApiKeyStatuses;

/// <summary>
/// Handles <see cref="ListApiKeyStatusesQuery"/> by naming every value the status filter accepts, in
/// the order <see cref="ApiKeyStatusFilters.All"/> defines, for the request's culture.
/// </summary>
public sealed class ListApiKeyStatusesQueryHandler
{
    /// <summary>Names each status as an administrator reads it, in the request's culture.</summary>
    private readonly ApiKeyStatusDisplayNames _statusNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="statusNames">Names an API key status for the request's culture.</param>
    public ListApiKeyStatusesQueryHandler(ApiKeyStatusDisplayNames statusNames)
    {
        _statusNames = statusNames;
    }

    /// <summary>Returns every status the filter accepts, named, in the filter vocabulary's order.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; the vocabulary is in code, so this never fails.</returns>
    public Task<Result<IReadOnlyList<ApiKeyChoiceDto>>> HandleAsync(
        ListApiKeyStatusesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApiKeyChoiceDto> choices = ApiKeyStatusFilters.All
            .Select(status =>
            {
                return new ApiKeyChoiceDto(status, _statusNames.Of(status));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<ApiKeyChoiceDto>>.Ok(choices));
    }
}
