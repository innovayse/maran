namespace Maran.Agent.Client.Services.DbService;

/// <summary>One grant-table row the repair deliberately left exactly as it found it.</summary>
/// <param name="GrantHost">The server's <c>Host</c> column, verbatim.</param>
/// <param name="DatabaseName">
/// The server's <c>Db</c> column, verbatim — escapes included. It is NOT tidied: a row lands here
/// precisely because it is not a value this panel could have produced, and an operator deciding what
/// to do about it needs the bytes the server holds rather than a rendering of them.
/// </param>
/// <param name="DbUsername">The server's <c>User</c> column, verbatim.</param>
/// <param name="Reason">
/// Why the row was refused, as the machine-stable name of the agent's <c>GrantRepairRefusal</c>
/// value — <c>HostIsNotLocalhost</c>, <c>NotThePanelsNaming</c>, <c>UnrecognisedPrivileges</c>,
/// <c>PartiallyOrUnfamiliarlyEscaped</c>, or <c>Unspecified</c> for a value this build was not
/// compiled knowing about.
/// <para>
/// A string and not the wire enum, so that an agent newer than this panel widens the set without the
/// panel mistaking an unknown value for the zero one. The panel names it for an operator by resource
/// key and falls back to this identifier, which is loud rather than wrong.
/// </para>
/// </param>
/// <remarks>
/// <b>These three names belong to whoever wrote the row, which on a real host is frequently not the
/// account whose screen is about to show them.</b> The agent reads the whole of the server's
/// database-level grant table; a refused row can name another tenant's database and user, or a DBA's
/// own monitoring credential. That is why the panel exposes this only to an administrator
/// (<c>DatabaseGrantsController</c>) and never to a customer.
/// </remarks>
public sealed record RefusedGrantDto(
    string GrantHost,
    string DatabaseName,
    string DbUsername,
    string Reason);
