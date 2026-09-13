namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>Reads the most recent audit journal entries, newest first.</summary>
/// <param name="Limit">
/// How many rows to return. Required rather than optional: the journal only grows, so a caller
/// that could omit the bound would eventually ask for the whole table.
/// </param>
public sealed record ListAuditEventsQuery(int Limit);
