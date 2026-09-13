using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Common;

/// <summary>Everything one chart screen needs: which range it covers and the points to draw.</summary>
/// <remarks>
/// The range is echoed back rather than left implicit. A slow request for seven days can land after
/// the operator has already switched to twenty-four hours, and without the echo the screen has no
/// way to tell that the points it just received answer a question it is no longer asking.
///
/// An empty bucket list is an ordinary answer, not a failure: a panel installed ten minutes ago has
/// no samples yet, and the interface draws its empty state rather than an error.
/// </remarks>
/// <param name="Range">The range these buckets cover.</param>
/// <param name="BucketSeconds">How wide each bucket is, so the interface can label the axis without knowing the rule.</param>
/// <param name="Buckets">The points, oldest first.</param>
public sealed record MetricsChartDto(ChartRange Range, int BucketSeconds, IReadOnlyList<MetricBucketDto> Buckets);
