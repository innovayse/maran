namespace Maran.Modules.Ssl.Domain.Entities;

/// <summary>
/// The trusted certificate the panel serves its own interface with: which host name it was ordered
/// for, when it expires and when it was last installed (issue #132).
/// </summary>
/// <remarks>
/// Server-wide and not per-customer, so it carries no <c>AccountId</c> and no tenant filter, like
/// <see cref="AcmeAccount"/>: the panel's own TLS belongs to the operator. Keyed by the host name,
/// because a certificate is valid for the name it was ordered for and for nothing else — a panel whose
/// configured name changed has no certificate for the new name until one is issued, and renewal
/// renews only the row for the name the panel is configured with today.
///
/// No row means the panel serves the installer's self-signed pair, which is the fallback: a failed
/// issuance or renewal never writes one, and never removes the material already installed.
/// </remarks>
public sealed class PanelCertificate
{
    /// <summary>The host name the certificate was ordered for; the row's identity.</summary>
    public string HostName { get; private set; }

    /// <summary>When the installed certificate expires, read from the certificate by the agent.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the certificate now on disk was installed.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Records the first certificate installed for a host name.</summary>
    /// <param name="hostName">The host name the certificate was ordered for.</param>
    /// <param name="expiresAt">When the installed certificate expires.</param>
    /// <param name="issuedAt">When it was installed, taken from <see cref="IClock"/>.</param>
    public PanelCertificate(string hostName, DateTimeOffset expiresAt, DateTimeOffset issuedAt)
    {
        HostName = hostName;
        ExpiresAt = expiresAt;
        IssuedAt = issuedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private PanelCertificate()
    {
        HostName = string.Empty;
    }

    /// <summary>Records that a new certificate for the same name replaced the one on disk.</summary>
    /// <param name="expiresAt">When the new certificate expires.</param>
    /// <param name="issuedAt">When it was installed, taken from <see cref="IClock"/>.</param>
    public void Reissued(DateTimeOffset expiresAt, DateTimeOffset issuedAt)
    {
        ExpiresAt = expiresAt;
        IssuedAt = issuedAt;
    }

    /// <summary>Whether the certificate is inside the renewal window at <paramref name="now"/>.</summary>
    /// <param name="now">The current time, from <see cref="IClock"/>.</param>
    /// <param name="window">How far ahead of expiry a certificate is re-ordered.</param>
    /// <returns>True when it expires within <paramref name="window"/> of <paramref name="now"/>.</returns>
    public bool IsDueForRenewal(DateTimeOffset now, TimeSpan window)
    {
        return ExpiresAt - now <= window;
    }
}
