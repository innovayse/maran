using Maran.Modules.Databases.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Databases.Queries.ListDatabaseUsers;

/// <summary>Holds a <see cref="ListDatabaseUsersQuery"/> to the paging contract and to its own sort whitelist.</summary>
public sealed class ListDatabaseUsersQueryValidator : PagedQueryValidator<ListDatabaseUsersQuery, DatabaseUser>
{
    /// <summary>Binds the shared paging rules to this list's sort map.</summary>
    public ListDatabaseUsersQueryValidator()
        : base(ListDatabaseUsersSorts.Map)
    {
    }
}
