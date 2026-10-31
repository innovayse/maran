using Maran.Modules.Identity.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>
/// Holds <see cref="ListSessionsQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The two server-established members are not validated here: the controller writes them from the
/// caller's token, and an empty <see cref="ListSessionsQuery.UserId"/> matches no row.
/// </remarks>
public sealed class ListSessionsQueryValidator : PagedQueryValidator<ListSessionsQuery, Session>
{
    /// <summary>Declares the paging rules against <see cref="ListSessionsSorts.Map"/>.</summary>
    public ListSessionsQueryValidator()
        : base(ListSessionsSorts.Map)
    {
    }
}
