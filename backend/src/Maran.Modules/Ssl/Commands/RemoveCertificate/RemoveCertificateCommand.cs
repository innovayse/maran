namespace Maran.Modules.Ssl.Commands.RemoveCertificate;

/// <summary>
/// Removes a certificate: its material from the agent's store, its TLS block from the site's vhost,
/// and its row (spec §11).
/// </summary>
/// <param name="Id">The certificate to remove. Another customer's answers 404, never 403.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record RemoveCertificateCommand(Guid Id, string IpAddress, string UserAgent);
