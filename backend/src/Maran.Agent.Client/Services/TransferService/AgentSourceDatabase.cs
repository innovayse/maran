namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One database found on a source.</summary>
/// <param name="Name">The database name, validated by the agent as a database name.</param>
/// <param name="SizeBytes">Its size in bytes, or <c>null</c> when it was not measured.</param>
public sealed record AgentSourceDatabase(string Name, long? SizeBytes);
