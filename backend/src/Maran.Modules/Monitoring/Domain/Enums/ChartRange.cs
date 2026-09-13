namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>How far back a chart reaches, and therefore how coarsely it is bucketed.</summary>
/// <remarks>
/// Two ranges and not an arbitrary window, because the pair is what the retention window can
/// actually answer: raw samples are kept for seven days (R10), so a request for a month would draw
/// a line that stops partway across with nothing saying why. The bucket width travels with the
/// range rather than being a second parameter — the two are one decision, and a caller free to ask
/// for seven days in five-minute buckets would be asking for two thousand points nothing can draw.
/// </remarks>
public enum ChartRange
{
    /// <summary>The last twenty-four hours, bucketed to five minutes.</summary>
    LastDay = 1,

    /// <summary>The last seven days — the whole retention window — bucketed to thirty minutes.</summary>
    LastWeek = 2,
}
