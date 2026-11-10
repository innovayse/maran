namespace Maran.Agent.Client.Services.TransferService;

/// <summary>What one pull of an account's files from a registered source needs (issue #197, phase 2).</summary>
/// <param name="Endpoint">Where the source is.</param>
/// <param name="Credential">The key to connect with and the host key to require; a secret, held for the call.</param>
/// <param name="AccountUsername">The system user name of the account on THIS host whose home receives the files.</param>
/// <param name="SourceHome">The account's home directory on the source.</param>
/// <param name="Phase">Which pass this is.</param>
/// <param name="BandwidthLimitKib">The most KiB per second to read, or 0 for no limit.</param>
/// <param name="ExpectedBytes">How many bytes are expected to arrive, for the free-space check, or 0 when unknown.</param>
public sealed record AgentFilePullRequest(
    AgentSourceEndpoint Endpoint,
    AgentSourceCredential Credential,
    string AccountUsername,
    string SourceHome,
    AgentPullPhase Phase,
    int BandwidthLimitKib,
    long ExpectedBytes);
