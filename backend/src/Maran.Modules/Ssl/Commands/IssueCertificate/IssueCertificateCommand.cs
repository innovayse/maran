namespace Maran.Modules.Ssl.Commands.IssueCertificate;

/// <summary>
/// Orders a certificate for one of the caller's sites from the configured ACME authority and installs
/// it (spec §11).
/// </summary>
/// <param name="Domain">The domain to issue for. It must be a site the caller owns.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record IssueCertificateCommand(string Domain, string IpAddress, string UserAgent);
