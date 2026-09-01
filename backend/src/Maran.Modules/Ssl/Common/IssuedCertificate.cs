namespace Maran.Modules.Ssl.Common;

/// <summary>
/// Freshly issued certificate material on its way from the authority to the agent: the leaf and its
/// chain, the private key that matches them, and when the leaf expires.
/// </summary>
/// <remarks>
/// A class and not a record, and that is the whole reason this file exists rather than a one-line
/// declaration. A positional record synthesises a <c>ToString</c> that prints every property, so a
/// record here would put a private key into any log line, exception message or debugger dump that
/// ever interpolated it — and it would do so silently, because nobody writes
/// <c>$"{material}"</c> expecting a key. <see cref="ToString"/> below is overridden to describe the
/// material without revealing it, and the type carries no other formatting of itself.
///
/// The material is held for the length of one install and then dropped. It is never stored: the key
/// belongs in the agent's certificate store, not in the panel's database, its backups, or its logs
/// (rules/security.md item 8).
/// </remarks>
public sealed class IssuedCertificate
{
    /// <summary>PEM-encoded leaf certificate, followed by its chain.</summary>
    public string CertificatePem { get; }

    /// <summary>PEM-encoded private key matching <see cref="CertificatePem"/>. Never logged, never stored.</summary>
    public string PrivateKeyPem { get; }

    /// <summary>When the leaf certificate expires.</summary>
    public DateTimeOffset NotAfter { get; }

    /// <summary>Creates the material.</summary>
    /// <param name="certificatePem">PEM-encoded leaf certificate, followed by its chain.</param>
    /// <param name="privateKeyPem">PEM-encoded private key matching the certificate.</param>
    /// <param name="notAfter">When the leaf certificate expires.</param>
    public IssuedCertificate(string certificatePem, string privateKeyPem, DateTimeOffset notAfter)
    {
        CertificatePem = certificatePem;
        PrivateKeyPem = privateKeyPem;
        NotAfter = notAfter;
    }

    /// <summary>Describes the material without revealing any of it.</summary>
    /// <returns>A fixed sentence naming the type and the expiry, and no material at all.</returns>
    public override string ToString()
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"IssuedCertificate(expires {NotAfter:O})");
    }
}
