using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Demo.Options;

/// <summary>
/// Settings of the public demo, bound from the <c>Demo</c> configuration section and validated at start.
/// </summary>
/// <remarks>
/// The budgets are starting values (threat note T6) to be re-measured. A reset is a restart of the
/// process, so every counter below lives for one reset window.
/// </remarks>
public sealed class DemoOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Demo";

    /// <summary>
    /// Where a visitor reports abusive content: a bare e-mail address or an <c>https</c> URL. Required in demo
    /// mode (<see cref="DemoOptionsValidator"/> refuses an empty or malformed value, so the demo does not start
    /// without one), shown on the banner and returned by the status endpoint. It names a role mailbox of the
    /// operator, never a person; there is no default in the code, and <c>demo/panel.env.example</c> carries the
    /// documented placeholder.
    /// </summary>
    public string AbuseContact { get; set; } = string.Empty;

    /// <summary>Hours between resets; the status endpoint turns it into the next reset time.</summary>
    [Range(1, 168)]
    public int ResetIntervalHours { get; set; } = 2;

    /// <summary>Counted mutations the whole demo accepts in one reset window.</summary>
    [Range(1, 1_000_000)]
    public int GlobalMutationBudget { get; set; } = 5_000;

    /// <summary>Counted mutations one visitor may make per <see cref="VisitorWindowSeconds"/>.</summary>
    [Range(1, 10_000)]
    public int VisitorMutationsPerWindow { get; set; } = 60;

    /// <summary>Length of the per-visitor window, in seconds.</summary>
    [Range(1, 3_600)]
    public int VisitorWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Sign-ins, refreshes and sign-outs one visitor may make per <see cref="VisitorWindowSeconds"/>. Each
    /// sign-in or refresh writes a session row and an audit row, so they are bounded apart from the
    /// mutation budget, which they must never spend.
    /// </summary>
    [Range(1, 10_000)]
    public int VisitorSignInsPerWindow { get; set; } = 20;

    /// <summary>
    /// Sign-ins, refreshes and sign-outs the whole demo accepts per <see cref="VisitorWindowSeconds"/>:
    /// a ceiling on how fast the session and audit tables can grow, however many visitors there are.
    /// </summary>
    [Range(1, 1_000_000)]
    public int GlobalSignInsPerWindow { get; set; } = 600;

    /// <summary>Visitors the rate table follows at once; further ones share one overflow bucket.</summary>
    [Range(1, 1_000_000)]
    public int MaxTrackedVisitors { get; set; } = 10_000;

    /// <summary>Audit rows refusals may write in one reset window; later ones are refused but not journaled.</summary>
    [Range(0, 100_000)]
    public int AuditedRefusalsPerWindow { get; set; } = 200;
}
