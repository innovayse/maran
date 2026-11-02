namespace Maran.Sdk.Events;

/// <summary>
/// A subscriber ran the cull and the host counted the processes it signalled. Zero is a real answer
/// and a different one from the other two readings: the cull ran and matched no process.
/// </summary>
/// <param name="Count">How many of the account's processes were signalled.</param>
public sealed record SessionCullEnded(uint Count) : SessionCullReading;
