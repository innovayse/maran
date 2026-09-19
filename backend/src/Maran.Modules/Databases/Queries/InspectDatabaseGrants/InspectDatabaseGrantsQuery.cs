namespace Maran.Modules.Databases.Queries.InspectDatabaseGrants;

/// <summary>
/// Reads this server's whole database-level grant table and reports what a repair WOULD change,
/// changing nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes no parameter, and there is none to take.</b> The operation is host-wide: one database
/// server, one grant table, every customer on it at once. In particular it takes no account — a
/// per-account report is not a thing the server can answer, because the server has no notion of a
/// tenant and a refused row is refused precisely because its names decode to no account this panel
/// owns.
/// </para>
/// <para>
/// <b>This is the inspection half of a pair, and the pair is deliberate.</b> The repair that acts is
/// <c>RepairDatabaseGrantsCommand</c>, and it will not act on figures the caller has not read: it
/// re-runs this same classification and refuses when the count has moved. So this query is not a
/// convenience beside a button — it is the only way in.
/// </para>
/// </remarks>
public sealed record InspectDatabaseGrantsQuery;
