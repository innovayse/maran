using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Domain.ValueObjects;
using Maran.Modules.Monitoring.Mappers;
using Maran.Modules.Monitoring.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Maran.Modules.Monitoring.Queries.GetMetricsChart;

/// <summary>
/// Handles <see cref="GetMetricsChartQuery"/> by bucketing the raw samples in PostgreSQL and
/// deriving the network rates from the result.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bucketing happens on READ, and there is no rollup table (R10).</b> Seven days of
/// once-a-minute samples is about ten thousand rows — small enough that PostgreSQL groups them
/// faster than the browser draws the answer, and far smaller than the second write path, the second
/// retention rule and the standing risk of disagreement that a summary table would cost.
/// </para>
/// <para>
/// <b>The SQL is raw because <c>date_bin</c> has no LINQ translation</b>, and it is parameterised:
/// the bucket width, the anchor and the range start all travel as typed parameters, and the only
/// identifiers written into the text are this module's own table and columns. Nothing a caller sends
/// reaches the statement — the caller sends one enum, which chooses between two fixed windows.
/// </para>
/// <para>
/// <b>The level metrics are averaged in SQL; the network counters are not, and must not be.</b> The
/// mean of a monotonically rising counter is a number with no physical meaning. What the SQL takes
/// instead is the LAST counter reading in each bucket and the instant it was taken, and
/// <see cref="NetworkRateCalculator"/> then derives a rate from consecutive buckets — dividing by
/// the seconds that actually separate them and clamping a negative difference to zero (R7). Doing it
/// that way, rather than inside the aggregate, is also what makes the arithmetic testable without a
/// database.
/// </para>
/// </remarks>
public sealed class GetMetricsChartQueryHandler
{
    /// <summary>
    /// The bucketing statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>// raw-sql: date_bin has no LINQ translation, and neither does "the last value in each
    /// group ordered by time". Both are the whole point of this query (R10), so the alternative is
    /// not different LINQ but streaming every raw row to the application and grouping it there.</c>
    /// </para>
    /// <para>
    /// Identifiers are quoted because the panel's tables and columns are PascalCase and PostgreSQL
    /// folds unquoted names to lower case (rules/csharp.md "Database naming"). The schema is
    /// <see cref="MonitoringDbContext.SchemaName"/>, written out here because a statement cannot
    /// parameterise an identifier.
    /// </para>
    /// <para>
    /// <c>(array_agg(x ORDER BY t DESC))[1]</c> is "the newest reading in this bucket". PostgreSQL
    /// has no <c>last()</c> aggregate, and a window function would need a second pass over the same
    /// grouping.
    /// </para>
    /// <para>
    /// The averages are cast to <c>double precision</c>: <c>AVG</c> of a <c>bigint</c> returns
    /// <c>numeric</c>, which crosses as a decimal, and every one of these figures is about to be
    /// drawn on a chart.
    /// </para>
    /// </remarks>
    private const string BucketSql = """
        SELECT
            date_bin(@bucket, s."CapturedAt", @origin) AS "BucketStart",
            AVG(s."CpuPercent")::double precision AS "CpuPercent",
            AVG(s."MemoryUsedBytes")::double precision AS "MemoryUsedBytes",
            AVG(s."MemoryTotalBytes")::double precision AS "MemoryTotalBytes",
            AVG(s."DiskUsedBytes")::double precision AS "DiskUsedBytes",
            AVG(s."DiskTotalBytes")::double precision AS "DiskTotalBytes",
            AVG(s."LoadAverage1m")::double precision AS "LoadAverage1m",
            (array_agg(s."NetworkRxBytes" ORDER BY s."CapturedAt" DESC))[1] AS "NetworkRxBytes",
            (array_agg(s."NetworkTxBytes" ORDER BY s."CapturedAt" DESC))[1] AS "NetworkTxBytes",
            MAX(s."CapturedAt") AS "LastCapturedAt"
        FROM monitoring."Samples" AS s
        WHERE s."CapturedAt" >= @since
        GROUP BY 1
        ORDER BY 1
        """;

    /// <summary>The module's database context, which owns the samples.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>The panel's clock; the range is measured back from its now.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock, which decides where the range starts.</param>
    public GetMetricsChartQueryHandler(MonitoringDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Returns the chart for the requested range.</summary>
    /// <param name="query">The validated range request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The chart. An empty bucket list is a success, not a failure: a panel installed ten minutes ago
    /// has no samples yet, and the interface draws its empty state.
    /// </returns>
    public async Task<Result<MetricsChartDto>> HandleAsync(
        GetMetricsChartQuery query,
        CancellationToken cancellationToken)
    {
        var window = ChartWindow.For(query.Range);
        var since = _clock.UtcNow - window.Lookback;

        var rows = await _dbContext.MetricBuckets
            .FromSqlRaw(
                BucketSql,
                new NpgsqlParameter("bucket", NpgsqlDbType.Interval) { Value = window.Bucket },
                new NpgsqlParameter("origin", NpgsqlDbType.TimestampTz) { Value = ChartWindow.BucketOrigin },
                new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = since })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Result<MetricsChartDto>.Ok(MetricsChartMapper.Create(query.Range, rows));
    }
}
