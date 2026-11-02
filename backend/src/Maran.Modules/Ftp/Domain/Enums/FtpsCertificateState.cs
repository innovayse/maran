namespace Maran.Modules.Ftp.Domain.Enums;

/// <summary>
/// What the agent found at the certificate path of the hostname the panel asked about: one fact with
/// three outcomes, not two booleans of which the second only means something when the first is true.
/// </summary>
/// <remarks>
/// With no hostname persisted the agent is asked about nothing and answers <see cref="Absent"/>; a
/// screen that must tell "no hostname yet" from "no material for it" reads this together with the
/// status's <c>Hostname</c>, which is <c>null</c> in exactly that case.
/// </remarks>
public enum FtpsCertificateState
{
    /// <summary>No certificate material exists for the hostname.</summary>
    Absent = 1,

    /// <summary>The material is the agent's own self-signed placeholder: every customer's client will warn.</summary>
    SelfSigned = 2,

    /// <summary>The material is a certificate somebody installed, not the agent's placeholder.</summary>
    Provided = 3,
}
