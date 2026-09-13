namespace Maran.Agent.Client.Services.SslService;

/// <summary>What installing a certificate produced on the server.</summary>
/// <param name="ExpiresAt">
/// When the installed certificate expires, parsed by the agent from the certificate itself, so the
/// panel can schedule renewal from what is actually on disk rather than from what it ordered.
/// </param>
public sealed record InstalledCertificateDto(DateTimeOffset ExpiresAt);
