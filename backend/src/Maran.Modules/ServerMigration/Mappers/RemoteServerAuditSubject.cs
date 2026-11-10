using Maran.Modules.ServerMigration.Domain.Entities;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Spells what a remote-server audit entry names: the server's address and identifier, and nothing that is a
/// credential. The entry is append-only and read by the operator, so it never carries a key — private or public.
/// </summary>
public static class RemoteServerAuditSubject
{
    /// <summary>The subject of an entry about a registered server.</summary>
    /// <param name="server">The server.</param>
    /// <returns>The server's host and identifier.</returns>
    public static string Of(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return $"{server.Host} ({server.Id})";
    }

    /// <summary>The subject of an entry about a registration that was refused before a server existed.</summary>
    /// <param name="host">The normalised host the registration named.</param>
    /// <param name="port">The port it named.</param>
    /// <returns>The address that was attempted.</returns>
    public static string OfAttempt(string host, int port)
    {
        return $"{host}:{port}";
    }

    /// <summary>The subject of an entry about a server that could not be found.</summary>
    /// <param name="serverId">The identifier the caller named.</param>
    /// <returns>The identifier, so the probe leaves a trace.</returns>
    public static string OfMissing(Guid serverId)
    {
        return serverId.ToString();
    }
}
