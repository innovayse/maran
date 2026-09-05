using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Queries.GetMetricsChart;

/// <summary>Reads the stored samples for a range, already bucketed into points a chart can draw.</summary>
/// <remarks>
/// The range is the only parameter, and the bucket width is NOT one. The two are a single decision
/// (see <c>ChartWindow</c>): a caller free to ask for seven days in five-minute buckets would be
/// asking for two thousand points nothing can render, and a caller free to ask for a month would get
/// a line that stops after seven days because that is all the retention window keeps.
/// </remarks>
/// <param name="Range">How far back the chart reaches.</param>
public sealed record GetMetricsChartQuery(ChartRange Range);
