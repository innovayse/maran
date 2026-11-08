namespace Maran.Agent.Simulated.State;

/// <summary>The certificate the simulated host's panel vhost names. As with sites, only its dates are kept.</summary>
/// <param name="ExpiresAt">When the certificate stops being valid.</param>
/// <param name="SelfSigned">True for the placeholder the host generates for itself.</param>
/// <param name="InstalledAt">When it was put in place.</param>
public sealed record SimulatedPanelCertificate(DateTimeOffset ExpiresAt, bool SelfSigned, DateTimeOffset InstalledAt);
