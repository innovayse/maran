using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Domain.ValueObjects;

/// <summary>How far back a chart reaches and how wide its buckets are — one decision, one type.</summary>
/// <remarks>
/// <para>
/// The pairing is deliberate (R10). Raw samples are kept for seven days at roughly one a minute, so
/// a day holds about 1,440 of them and a week about 10,080 — neither of which can be drawn. Five
/// minutes over a day and thirty minutes over a week both land near 290 points, which is about one
/// per pixel of a chart on a laptop: fine enough that nothing an operator would act on is averaged
/// away, coarse enough that the browser draws it in one frame.
/// </para>
/// <para>
/// There is no rollup table behind this. The buckets are computed on read, by PostgreSQL's
/// <c>date_bin</c>, over the raw rows — so there is one copy of the data, no second write path per
/// sample, and no possibility of the summary and the samples disagreeing.
/// </para>
/// </remarks>
/// <param name="Lookback">How far back from now the chart reaches.</param>
/// <param name="Bucket">How wide each bucket is.</param>
public sealed record ChartWindow(TimeSpan Lookback, TimeSpan Bucket)
{
    /// <summary>
    /// The fixed anchor <c>date_bin</c> measures its buckets from.
    /// </summary>
    /// <remarks>
    /// A constant instant rather than the start of the requested range, so that two requests a
    /// minute apart return the SAME bucket boundaries. Anchoring on "now" would shift every boundary
    /// on every refresh, and a chart whose points move sideways while its data does not is one an
    /// operator cannot compare against the one they took a screenshot of.
    /// </remarks>
    public static readonly DateTimeOffset BucketOrigin = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Resolves the window a range asks for.</summary>
    /// <param name="range">The range the caller asked for.</param>
    /// <returns>The lookback and bucket width for that range.</returns>
    /// <remarks>
    /// The default arm answers with the day window rather than throwing. The value arrives from a
    /// query whose validator has already refused anything outside the enum, so an unmatched value
    /// here would be a bug in this file — and a chart is not the place to surface one as a 500.
    /// </remarks>
    public static ChartWindow For(ChartRange range)
    {
        return range switch
        {
            ChartRange.LastWeek => new ChartWindow(TimeSpan.FromDays(7), TimeSpan.FromMinutes(30)),
            _ => new ChartWindow(TimeSpan.FromHours(24), TimeSpan.FromMinutes(5)),
        };
    }
}
