namespace Maran.Modules.Monitoring.Common;

/// <summary>How many package updates the host's package manager reports as pending.</summary>
/// <param name="Count">
/// The number of pending updates. A count is only ever sent for a read that succeeded: a host whose
/// package cache could not be read answers with an error, never with a zero here.
/// </param>
/// <param name="SecurityCount">
/// How many of the pending updates the host's package manager marks as security fixes, or <c>null</c>
/// when that was not measured: an agent that predates the classification, or a host whose security
/// reading failed, leaves at least one update unclassified, and a number built from a partial
/// reading would understate. Null and never zero, so the interface draws nothing rather than "no
/// security updates". Zero is only ever sent when every pending update was classified and none was
/// a security fix, or when nothing is pending at all.
/// </param>
public sealed record PendingUpdateCountDto(int Count, int? SecurityCount);
