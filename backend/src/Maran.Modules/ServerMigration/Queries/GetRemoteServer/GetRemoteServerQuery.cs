namespace Maran.Modules.ServerMigration.Queries.GetRemoteServer;

/// <summary>Reads one registered remote server in full. Built by the action from the route, never bound from a request.</summary>
/// <param name="ServerId">The server.</param>
public sealed record GetRemoteServerQuery(Guid ServerId);
