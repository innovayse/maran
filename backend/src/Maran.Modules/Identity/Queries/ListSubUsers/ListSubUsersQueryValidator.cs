using Maran.Modules.Identity.Models;
using Maran.Sdk.Validators;

namespace Maran.Modules.Identity.Queries.ListSubUsers;

/// <summary>Holds <see cref="ListSubUsersQuery"/> to the paging bounds only the list knows.</summary>
public sealed class ListSubUsersQueryValidator : PagedQueryValidator<ListSubUsersQuery, SubUserRow>
{
    /// <summary>Wires the validator to this list's sort whitelist.</summary>
    public ListSubUsersQueryValidator()
        : base(ListSubUsersSorts.Map)
    {
    }
}
