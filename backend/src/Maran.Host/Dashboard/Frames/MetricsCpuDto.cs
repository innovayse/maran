namespace Maran.Host.Dashboard.Frames;

/// <summary>The processor part of a <c>metrics</c> event.</summary>
/// <param name="Total">Utilisation across all cores, 0.0-100.0.</param>
/// <param name="Load">
/// The load averages over one, five and fifteen minutes, in that order. An entry the source did not
/// record is <c>null</c> and never zero: a point rebuilt from a stored one-minute sample has only the
/// first.
/// </param>
public sealed record MetricsCpuDto(double Total, IReadOnlyList<double?> Load);
