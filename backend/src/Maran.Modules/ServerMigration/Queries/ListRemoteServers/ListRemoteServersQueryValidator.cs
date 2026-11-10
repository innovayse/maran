using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListRemoteServers;

/// <summary>Holds <see cref="ListRemoteServersQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
/// <remarks>
/// The state filter needs no rule of its own: it is an exact match against the names of a closed set, so a spelling
/// that is none of them matches nothing, which is the honest answer to a state no server can be in.
/// </remarks>
public sealed class ListRemoteServersQueryValidator : PagedQueryValidator<ListRemoteServersQuery, RemoteServer>
{
    /// <summary>Declares the paging rules against <see cref="ListRemoteServersSorts.Map"/>.</summary>
    public ListRemoteServersQueryValidator()
        : base(ListRemoteServersSorts.Map)
    {
    }
}
