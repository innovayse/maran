namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>
/// Whether the FTPS daemon's live configuration forces TLS on both the control and the data channel:
/// one fact with three honest outcomes, where the wire used to spell it as a boolean whose
/// <c>false</c> meant both "TLS is optional" and "nobody could tell".
/// </summary>
public enum FtpsTlsEnforcement
{
    /// <summary>The live configuration was read and forces TLS on both channels.</summary>
    Enforced,

    /// <summary>
    /// The live configuration was read and TLS is optional on at least one channel: passwords may
    /// travel in the clear.
    /// </summary>
    NotEnforced,

    /// <summary>
    /// The agent could not read the live configuration (none exists yet, or the file could not be
    /// resolved), so nothing is known about enforcement either way.
    /// </summary>
    NotObserved,
}
