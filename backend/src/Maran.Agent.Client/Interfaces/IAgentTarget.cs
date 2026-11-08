namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Which server the agent calls made from the current flow of execution are addressed to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The local server is the default.</b> With no scope open, <see cref="Current"/> is
/// <see cref="Maran.SharedKernel.Constants.FleetServers.LocalId"/> and a call reaches the agent on the
/// panel's own host over the unix socket, exactly as it did before managed servers existed. A caller
/// that knows its server opens a scope with <see cref="Begin"/>.
/// </para>
/// <para>
/// <b>A scope belongs to the method that opens it.</b> The value flows DOWN into everything the method
/// awaits and never back UP out of an awaited callee, so <see cref="Begin"/> is called in the method
/// that makes the agent calls (or in one of its callers), and the scope is disposed there. A callee
/// that opens its own scope changes nothing for its caller.
/// </para>
/// </remarks>
public interface IAgentTarget
{
    /// <summary>The id of the server the next agent call is addressed to.</summary>
    Guid Current { get; }

    /// <summary>Addresses every agent call made until the returned scope is disposed to a server.</summary>
    /// <param name="serverId">The server to address; <see cref="Maran.SharedKernel.Constants.FleetServers.LocalId"/> for the panel's own.</param>
    /// <returns>
    /// The scope. Disposing it restores the target that was current when it was opened, so scopes nest
    /// and an unattended job can visit one server after another.
    /// </returns>
    IDisposable Begin(Guid serverId);
}
