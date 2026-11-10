namespace Maran.Agent.Client.Services.DnsService;

/// <summary>Whether a zone is signed, and in which phase: the panel's spelling of what the agent is told and reports.</summary>
public enum DnsSigningPhase
{
    /// <summary>The zone is not signed and carries no key material. What a zone that never had a key is sent as.</summary>
    Off,

    /// <summary>The zone is signed with the key it is sent with, under the panel's single-key policy.</summary>
    Active,

    /// <summary>
    /// The zone is being withdrawn from signing: the daemon follows its built-in insecure policy with the SAME
    /// key, so validating resolvers keep a consistent answer until the registrar's DS record is gone.
    /// </summary>
    Withdrawing,
}
