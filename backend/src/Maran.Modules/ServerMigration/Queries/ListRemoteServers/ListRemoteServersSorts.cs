using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListRemoteServers;

/// <summary>
/// The orders the remote servers list accepts. <see cref="ListRemoteServersQueryValidator"/> refuses any other sort
/// and <see cref="ListRemoteServersQueryHandler"/> orders by exactly these.
/// </summary>
public static class ListRemoteServersSorts
{
    /// <summary>
    /// <see cref="RemoteServerSortFields.Name"/>, <see cref="RemoteServerSortFields.Host"/>,
    /// <see cref="RemoteServerSortFields.State"/>, <see cref="RemoteServerSortFields.LastTestedAt"/> and
    /// <see cref="RemoteServerSortFields.CreatedAt"/>, by name when none is named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<RemoteServer> Map = new SortMapBuilder<RemoteServer>()
        .By(RemoteServerSortFields.Name, server => server.Name)
        .By(RemoteServerSortFields.Host, server => server.Host)
        .By(RemoteServerSortFields.State, server => server.State)
        .By(RemoteServerSortFields.LastTestedAt, server => server.LastTestedAt)
        .By(RemoteServerSortFields.CreatedAt, server => server.CreatedAt)
        .DefaultTo(RemoteServerSortFields.Name, SortDirection.Ascending)
        .TieBreaker(server => server.Id);
}
