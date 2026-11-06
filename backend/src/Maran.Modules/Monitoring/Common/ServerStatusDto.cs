namespace Maran.Modules.Monitoring.Common;

/// <summary>
/// The read-only server facts an integration holding the <c>server.read</c> key scope may ask for
/// (issue #144): what is pending on the host and whether it must be restarted.
/// </summary>
/// <param name="PendingUpdateCount">
/// How many package updates the host's package manager reports as pending. Only ever a figure from a
/// read that succeeded; a read that failed is the agent's typed error and never a zero here.
/// </param>
/// <param name="SecurityUpdateCount">
/// How many of the pending updates are security fixes, or <c>null</c> when that was not measured (an
/// agent that predates the classification, or a host whose security reading failed). Null and never
/// zero: a count over a partial reading would understate.
/// </param>
/// <param name="RebootRequired">
/// <c>true</c> or <c>false</c> when the host's own marker was read, or <c>null</c> when the agent did
/// not measure it. Null and never <c>false</c>, so an integration cannot mistake "not measured" for
/// "nothing to restart".
/// </param>
public sealed record ServerStatusDto(int? PendingUpdateCount, int? SecurityUpdateCount, bool? RebootRequired);
