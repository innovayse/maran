using FluentValidation;
using FluentValidation.Results;
using Maran.Host.Authorization;
using Maran.Host.Modules;
using Maran.Host.RateLimiting;
using Maran.Sdk.Contracts;
using Maran.Sdk.ModelBinding;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Host.Search;

/// <summary>
/// Publishes the global search: one question put to every module that can find things, answered as one
/// list of groups.
/// </summary>
/// <remarks>
/// <para>
/// <b>POST, with the term in the body.</b> A query string lands in browser history, in a <c>Referer</c>
/// and in any front proxy's access log, and a search box is where people paste addresses and, by
/// accident, secrets. The request is read-only and stores nothing, so it declares
/// <see cref="ViewAsAccess.SafeMutation"/>: an operator viewing the panel as a customer searches as that
/// customer. It is a minimal endpoint, outside the controller census of the view-as architecture test,
/// so the reason is written here and in the threat note.
/// </para>
/// <para>
/// <b>Authorization is the module query filters, and this file adds the door.</b> The route requires any
/// signed-in caller (a JWT; the API-key scheme is not part of the policy). What each caller may find is
/// decided inside each provider by its module's tenant filter, never by this endpoint, which hands the
/// providers no principal. The term is validated here, on the server: trimmed, free of control
/// characters, at most <c>PageDefaults.SearchMaxLength</c> and at least
/// <see cref="SearchQuery.MinimumTermLength"/> characters. Own rate-limit policy, per user.
/// </para>
/// </remarks>
public static class SearchEndpoint
{
    /// <summary>Maps <c>POST /api/v1/search</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/search",
            async (
                [FromBody] SearchRequest request,
                SearchFanOut fanOut,
                PackagedModuleComposition composition,
                CancellationToken cancellationToken) =>
            {
                var query = new SearchQuery(ValidTermOf(request));
                var groups = await fanOut.SearchAsync(
                    query,
                    provider => { return SearchModuleGate.Permits(composition, provider); },
                    SearchFanOut.ProviderTimeout,
                    cancellationToken);
                return Results.Ok(new SearchResultDto(groups));
            })
            .RequireAuthorization(RolePolicies.AnyAuthenticated)
            .RequireRateLimiting(SearchRateLimitPolicy.Name)
            .WithMetadata(new ViewAsAccessAttribute(ViewAsAccess.SafeMutation))
            .Produces<SearchResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    /// <summary>Reads the request's term, refusing one the search cannot answer.</summary>
    /// <param name="request">The bound body.</param>
    /// <returns>The trimmed term, at least <see cref="SearchQuery.MinimumTermLength"/> characters long.</returns>
    /// <exception cref="ValidationException">
    /// The term is missing or too short (<c>SearchTermTooShort</c>), too long (<c>ListSearchTooLong</c>) or
    /// carries a control character (<c>ListSearchInvalid</c>); the exception middleware answers 400 with
    /// the code, like every validation failure.
    /// </exception>
    private static string ValidTermOf(SearchRequest request)
    {
        var verdict = SearchTerm.Parse(request.Term, out var term);

        if (verdict == SearchTermVerdict.Accepted && !term.IsNone && term.Value.Length >= SearchQuery.MinimumTermLength)
        {
            return term.Value;
        }

        var code = SearchTermRefusal.CodeFor(verdict);

        throw new ValidationException([new ValidationFailure("term", code) { ErrorCode = code }]);
    }
}
