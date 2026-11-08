namespace Maran.Sdk.Contracts;

/// <summary>The processor part of one live host reading.</summary>
/// <param name="TotalPercent">Utilisation across all cores, 0.0-100.0.</param>
/// <param name="LoadAverage1m">System load average over the last minute.</param>
/// <param name="LoadAverage5m">
/// Load over the last five minutes, or <c>null</c> when the source did not record it: a reading rebuilt
/// from a stored one-minute sample has only the one-minute figure, and absent is not zero.
/// </param>
/// <param name="LoadAverage15m">Load over the last fifteen minutes, <c>null</c> on the same terms.</param>
public sealed record LiveCpuReading(
    double TotalPercent,
    double LoadAverage1m,
    double? LoadAverage5m,
    double? LoadAverage15m);
