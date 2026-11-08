namespace Maran.Modules.Accounts.Queries.CountCustomPlans;

/// <summary>
/// Counts the plans an administrator of this panel has made themselves: every plan the host owns
/// except the three standard ones a fresh installation ships with, for the first-run checklist.
/// </summary>
/// <remarks>
/// <para>
/// The three standard plans are seeded at start-up, so counting plans would tick the checklist's
/// plan step on a server nobody has touched. A reseller's plan is not counted either: it is that
/// reseller's work, not the administrator's.
/// </para>
/// <para>
/// Dispatched by the panel itself, never bound from a request, so it carries no validator.
/// </para>
/// </remarks>
public sealed record CountCustomPlansQuery;
