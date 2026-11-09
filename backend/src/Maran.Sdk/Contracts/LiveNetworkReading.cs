namespace Maran.Sdk.Contracts;

/// <summary>The network part of one live host reading: rates, not counters.</summary>
/// <param name="ReceiveBytesPerSecond">
/// Bytes received per second since the previous reading, or <c>null</c> when there is none to measure
/// against: a rate needs two readings, and the first has no rate, which a chart draws as a gap and not
/// as a second of silence.
/// </param>
/// <param name="SendBytesPerSecond">Bytes sent per second, on the same terms.</param>
public sealed record LiveNetworkReading(double? ReceiveBytesPerSecond, double? SendBytesPerSecond);
