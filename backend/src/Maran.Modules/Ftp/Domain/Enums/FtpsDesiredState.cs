namespace Maran.Modules.Ftp.Domain.Enums;

/// <summary>What the panel wants this server's FTPS daemon to be doing: an INTENTION, never a measurement.</summary>
/// <remarks>
/// Three values and not a boolean, because a server can be in three situations and only two of them
/// have a settings row. A <c>bool Enabled</c> read off a row that may not exist has to invent an
/// answer for the missing row, and the one it invented — <c>false</c> — made "never configured" read
/// exactly like "an administrator turned it off". The two are different sentences on the screen: the
/// first means nobody has chosen yet, the second means somebody chose.
/// </remarks>
public enum FtpsDesiredState
{
    /// <summary>No settings row exists: FTPS has never been turned on on this server.</summary>
    NeverConfigured = 1,

    /// <summary>The settings row says the panel wants the daemon running.</summary>
    On = 2,

    /// <summary>The settings row says an administrator turned the daemon off.</summary>
    Off = 3,
}
