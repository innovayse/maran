namespace Maran.Host.Dashboard.Frames;

/// <summary>The network part of a <c>metrics</c> event: rates, never counters.</summary>
/// <param name="RxBps">Bytes received per second, or absent for the first reading, which has no rate.</param>
/// <param name="TxBps">Bytes sent per second, on the same terms.</param>
public sealed record MetricsNetworkDto(double? RxBps, double? TxBps);
