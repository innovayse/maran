using Maran.Modules.Databases.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Databases.Queries.ListDatabases;

/// <summary>
/// Holds <see cref="ListDatabasesQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The list has no filter of its own to validate: <see cref="ListDatabasesQuery.AccountId"/> is a
/// <see cref="Guid"/> the binder has already parsed, and the tenant filter decides what it can reach.
/// </remarks>
public sealed class ListDatabasesQueryValidator : PagedQueryValidator<ListDatabasesQuery, Database>
{
    /// <summary>Declares the paging rules against <see cref="ListDatabasesSorts.Map"/>.</summary>
    public ListDatabasesQueryValidator()
        : base(ListDatabasesSorts.Map)
    {
    }
}
