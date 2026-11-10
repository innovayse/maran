namespace Maran.Agent.Client.Services.DnsService;

/// <summary>The signing instruction of one zone, sent with every publication so a rebuilt host serves what the registrar expects.</summary>
/// <param name="Phase">Which phase the zone is in.</param>
/// <param name="Key">The key. Required for <see cref="DnsSigningPhase.Active"/> and <see cref="DnsSigningPhase.Withdrawing"/>, null for <see cref="DnsSigningPhase.Off"/>.</param>
public sealed record DnsSigningDefinition(DnsSigningPhase Phase, DnsSigningKeyDefinition? Key);
