namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>
/// What the agent found at the certificate path of the hostname it was asked about: one fact with
/// three outcomes, where the wire spells it as two booleans of which the second only means something
/// when the first is true.
/// </summary>
public enum FtpsCertificate
{
    /// <summary>No certificate material exists for the hostname.</summary>
    Absent,

    /// <summary>The material is the agent's own self-signed placeholder, so every client will warn.</summary>
    SelfSigned,

    /// <summary>The material is a certificate somebody installed, not the agent's placeholder.</summary>
    Provided,
}
