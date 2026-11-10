namespace Maran.Modules.Ssl.Models;

/// <summary>
/// What the Ssl module says about one certificate when an account moves to another server (issue #127): the
/// name it covers, how it was obtained and when it ends, and nothing else.
/// </summary>
/// <remarks>
/// A certificate's key and chain live on the server and are never read here, so there is no field that could
/// carry one. The target uses this to tell the administrator what has to be issued or uploaded again
/// (decision D2); it creates no certificate from it.
/// </remarks>
/// <param name="Domain">The host name the certificate covers.</param>
/// <param name="Source">How it was obtained: the name of a <c>CertificateSource</c> member, <c>Acme</c> or <c>Custom</c>.</param>
/// <param name="IsWildcard">Whether it covers the host's subdomains as well.</param>
/// <param name="NotAfter">When it stops being valid.</param>
public sealed record CertificateMigrationItem(string Domain, string Source, bool IsWildcard, DateTimeOffset NotAfter);
