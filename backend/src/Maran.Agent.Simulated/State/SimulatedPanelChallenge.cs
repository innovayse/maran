namespace Maran.Agent.Simulated.State;

/// <summary>An HTTP-01 challenge file written for the panel's own host name.</summary>
/// <param name="Token">The challenge token, which names the file; the challenge's key.</param>
/// <param name="KeyAuthorization">The file's content.</param>
/// <param name="WrittenAt">When the file was written.</param>
public sealed record SimulatedPanelChallenge(string Token, string KeyAuthorization, DateTimeOffset WrittenAt);
