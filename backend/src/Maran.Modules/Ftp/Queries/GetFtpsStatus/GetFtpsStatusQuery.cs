namespace Maran.Modules.Ftp.Queries.GetFtpsStatus;

/// <summary>
/// Asks what this server's FTPS daemon is actually doing.
/// </summary>
/// <remarks>
/// It takes no parameter, and there is none to take: one server has one daemon, and the hostname the
/// answer describes is read from the panel's own settings row rather than supplied by the caller. A
/// caller-supplied hostname would let a screen ask about certificate material for a name this server
/// does not serve and render the answer as though it were this daemon's.
/// </remarks>
public sealed record GetFtpsStatusQuery();
