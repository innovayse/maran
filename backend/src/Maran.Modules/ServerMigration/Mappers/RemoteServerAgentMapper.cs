using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Restates a registered server as the endpoint and credential the agent connects with. The only place the stored
/// private key is read, and it leaves here inside <see cref="SensitiveString"/>, which prints as a mask.
/// </summary>
public static class RemoteServerAgentMapper
{
    /// <summary>The login and address the agent is told to look at, with no credential.</summary>
    /// <param name="server">The registered server.</param>
    /// <returns>The endpoint.</returns>
    public static AgentSourceEndpoint EndpointOf(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new AgentSourceEndpoint(server.Host, server.Port, server.SshUser);
    }

    /// <summary>The key to log in with and the host key to require, for a server whose host key is pinned.</summary>
    /// <param name="server">The registered server; its host key MUST be pinned.</param>
    /// <returns>The credential, held for the one call it is made for.</returns>
    /// <exception cref="InvalidOperationException">
    /// No host key is pinned. A caller reaches this method only after judging that, so reaching it with none is a bug
    /// and never a state a request can produce.
    /// </exception>
    public static AgentSourceCredential CredentialOf(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (server.HostKey is null)
        {
            throw new InvalidOperationException("A credential is built only for a server whose host key is pinned.");
        }

        return new AgentSourceCredential(new SensitiveString(server.PrivateKeyCipher), server.HostKey);
    }
}
