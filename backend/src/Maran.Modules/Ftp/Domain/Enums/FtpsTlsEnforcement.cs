namespace Maran.Modules.Ftp.Domain.Enums;

/// <summary>
/// Whether the FTPS daemon's live configuration forces TLS on both the control and the data channel:
/// one fact with three honest outcomes, because "TLS is optional" and "nobody could read the
/// configuration" are different things to tell an administrator and a boolean folded the second into
/// the first.
/// </summary>
/// <remarks>
/// <see cref="NotObserved"/> must never be rendered as <see cref="Enforced"/>, and is not rendered as
/// <see cref="NotEnforced"/> either: the second is a claim about the host that was measured, the
/// first is the admission that nothing was.
/// </remarks>
public enum FtpsTlsEnforcement
{
    /// <summary>The live configuration was read and forces TLS on both channels.</summary>
    Enforced = 1,

    /// <summary>
    /// The live configuration was read and TLS is optional on at least one channel: passwords may
    /// travel in the clear.
    /// </summary>
    NotEnforced = 2,

    /// <summary>The agent could not read the live configuration, so nothing is known about enforcement.</summary>
    NotObserved = 3,
}
