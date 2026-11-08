namespace Maran.Agent.Simulated.State;

/// <summary>
/// A certificate the simulated host holds for a domain. The record carries when it expires and never
/// the key or the certificate text: the simulation has nothing to serve them with, and a secret it
/// does not store cannot leak from it.
/// </summary>
/// <param name="AccountUsername">The owning account's system user name.</param>
/// <param name="Domain">The domain it was installed for; the certificate's key.</param>
/// <param name="ExpiresAt">When the certificate stops being valid.</param>
/// <param name="InstalledAt">When it was installed.</param>
public sealed record SimulatedCertificate(
    string AccountUsername,
    string Domain,
    DateTimeOffset ExpiresAt,
    DateTimeOffset InstalledAt);
