namespace Maran.Modules.Ssl.Domain.Enums;

/// <summary>Where a certificate came from, which decides whether the panel may replace it.</summary>
/// <remarks>
/// The distinction is not bookkeeping. Renewal re-orders a certificate and overwrites what is
/// installed, and doing that to material a customer uploaded would destroy a certificate the panel
/// cannot obtain again — so only <see cref="Acme"/> certificates are ever renewed automatically.
/// </remarks>
public enum CertificateSource
{
    /// <summary>Ordered by this panel from an ACME certificate authority, and renewable by it.</summary>
    Acme = 1,

    /// <summary>Supplied by the customer. Never re-ordered, never overwritten by renewal.</summary>
    Custom = 2,
}
