namespace Maran.Host.Dashboard.Frames;

/// <summary>What this server holds and what needs looking at, sent when either changes.</summary>
/// <param name="Counts">Accounts, sites and databases.</param>
/// <param name="Attention">
/// The attention block, read by the same <see cref="AttentionReader"/> as the landing page and the
/// navigation badge, so the three can never disagree.
/// </param>
public sealed record TotalsFrame(DashboardCountsDto Counts, DashboardAttentionDto Attention) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "totals";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
