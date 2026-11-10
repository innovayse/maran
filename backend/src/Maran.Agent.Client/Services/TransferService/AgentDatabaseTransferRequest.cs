namespace Maran.Agent.Client.Services.TransferService;

/// <summary>What the transfer of one database from a registered source needs (issue #197, phase 2).</summary>
/// <param name="Endpoint">Where the source is.</param>
/// <param name="Credential">The key to connect with and the host key to require; a secret, held for the call.</param>
/// <param name="SourceDatabase">The database's name on the source.</param>
/// <param name="AccountUsername">The system user name of the account on THIS host that owns the target database.</param>
/// <param name="TargetDatabase">The existing database on this host the dump is loaded into, with its owner's prefix.</param>
/// <param name="MaxBytes">The most dump bytes to accept, or 0 for the agent's own ceiling.</param>
public sealed record AgentDatabaseTransferRequest(
    AgentSourceEndpoint Endpoint,
    AgentSourceCredential Credential,
    string SourceDatabase,
    string AccountUsername,
    string TargetDatabase,
    long MaxBytes);
