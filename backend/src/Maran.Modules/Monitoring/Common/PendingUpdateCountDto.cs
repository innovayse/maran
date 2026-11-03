namespace Maran.Modules.Monitoring.Common;

/// <summary>How many package updates the host's package manager reports as pending.</summary>
/// <param name="Count">
/// The number of pending updates. A count is only ever sent for a read that succeeded: a host whose
/// package cache could not be read answers with an error, never with a zero here.
/// </param>
public sealed record PendingUpdateCountDto(int Count);
