namespace Maran.Modules.Ssl.Commands.InstallCustomCertificate;

/// <summary>
/// Installs certificate material the customer supplied, for one of their own sites (spec §11).
/// </summary>
/// <remarks>
/// The material is carried through this command and dropped after the install: it is never stored,
/// never journalled and never logged. That is also why this record deliberately holds the key as a
/// plain property and nothing else — a record's synthesised <c>ToString</c> prints every property, so
/// this type must never be interpolated into a message, and <see cref="ToString"/> below says so by
/// refusing to render the material.
/// </remarks>
/// <param name="Domain">The domain to install for. It must be a site the caller owns.</param>
/// <param name="CertificatePem">PEM-encoded leaf certificate, optionally followed by its chain.</param>
/// <param name="PrivateKeyPem">PEM-encoded private key matching the certificate. Never logged, never stored.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record InstallCustomCertificateCommand(
    string Domain,
    string CertificatePem,
    string PrivateKeyPem,
    string IpAddress,
    string UserAgent)
{
    /// <summary>Describes the command without revealing the material it carries.</summary>
    /// <returns>A sentence naming the operation and the domain, and no material at all.</returns>
    /// <remarks>
    /// Overriding this is the point of writing the record out by hand. The compiler's version prints
    /// every property, so one interpolation of this command — in a log line, an exception message, a
    /// debugger's watch window, a message-bus diagnostic — would print a customer's private key. The
    /// domain is kept because it is what makes a log line useful, and it is public information.
    /// </remarks>
    public override string ToString()
    {
        return $"InstallCustomCertificateCommand({Domain})";
    }
}
