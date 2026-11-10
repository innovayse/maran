using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Extensions;

/// <summary>Questions the commands that register and retarget a remote server both ask of the registry.</summary>
public static class RemoteServerQueryExtensions
{
    /// <summary>
    /// Finds whether another registered server already has the name, or already points at the same login on the
    /// same host and port.
    /// </summary>
    /// <param name="servers">The registry.</param>
    /// <param name="exceptServerId">
    /// The server being changed, which may keep its own name and endpoint; <see cref="Guid.Empty"/> when a server
    /// is being added.
    /// </param>
    /// <param name="name">The normalised name.</param>
    /// <param name="host">The normalised host.</param>
    /// <param name="port">The port.</param>
    /// <param name="sshUser">The normalised login.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// <c>RemoteServerNameTaken</c> or <c>RemoteServerEndpointTaken</c> as a conflict, or <c>null</c> when neither
    /// is taken. Both rules are also unique indexes; this answers before the agent is asked for anything, and a
    /// race that gets past it is answered by the same question asked again after the write failed.
    /// </returns>
    public static async Task<Error?> FindConflictAsync(
        this IQueryable<RemoteServer> servers,
        Guid exceptServerId,
        string name,
        string host,
        int port,
        string sshUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(servers);

        var others = servers.AsNoTracking().Where(server => server.Id != exceptServerId);

        if (await others.AnyAsync(server => server.Name == name, cancellationToken))
        {
            return Error.Of(nameof(ErrorMessages.RemoteServerNameTaken), ErrorType.Conflict);
        }

        if (await others.AnyAsync(
                server => server.Host == host && server.Port == port && server.SshUser == sshUser,
                cancellationToken))
        {
            return Error.Of(nameof(ErrorMessages.RemoteServerEndpointTaken), ErrorType.Conflict);
        }

        return null;
    }
}
