namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for certificate issuing, installation, removal and renewal actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class CertificateAuditActions
{
    /// <summary>A certificate was ordered from a certificate authority and installed.</summary>
    public const string CertificateIssued = "CertificateIssued";

    /// <summary>A certificate the customer supplied was installed.</summary>
    public const string CertificateInstalled = "CertificateInstalled";

    /// <summary>A certificate was removed and its site returned to plain HTTP.</summary>
    public const string CertificateRemoved = "CertificateRemoved";

    /// <summary>
    /// An unattended renewal ran for one certificate. Recorded on success and on failure alike:
    /// renewal has no operator watching it, so the journal is where a certificate that has quietly
    /// stopped renewing becomes visible before the site goes dark.
    /// </summary>
    public const string CertificateRenewed = "CertificateRenewed";
}
