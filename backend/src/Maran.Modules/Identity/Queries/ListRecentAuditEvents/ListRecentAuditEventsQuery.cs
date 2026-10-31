namespace Maran.Modules.Identity.Queries.ListRecentAuditEvents;

/// <summary>
/// Reads the few most recent audit journal entries, newest first — the landing page's excerpt. The
/// journal screen itself pages through <c>ListAuditEventsQuery</c>; this query exists because the
/// excerpt asks for a count no page size offers, and answers no total because it shows none.
/// </summary>
/// <param name="Limit">
/// How many rows to return. Required rather than optional: the journal only grows, so a caller
/// that could omit the bound would eventually ask for the whole table.
/// </param>
public sealed record ListRecentAuditEventsQuery(int Limit);
