namespace Maran.Modules.Accounts.Queries.InspectAccountHomeGroups;

/// <summary>
/// Reads every hosting account's home directory group and reports what a repair WOULD change,
/// changing nothing (issue #28 item E).
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes no parameter, and there is none to take.</b> The operation is host-wide: every hosting
/// account on the machine, at once. In particular it takes no account — a per-account report is not a
/// thing this repair answers, because the defect is a property of how an account WAS created, and
/// only the host's own password database and filesystem can say which accounts still carry it.
/// </para>
/// <para>
/// <b>This is the inspection half of a pair, and the pair is deliberate.</b> The repair that acts is
/// <c>RepairAccountHomeGroupsCommand</c>, and it will not act on figures the caller has not read: it
/// re-runs this same classification and refuses when the count has moved. So this query is not a
/// convenience beside a button — it is the only way in, mirroring
/// <c>Databases.Queries.InspectDatabaseGrants.InspectDatabaseGrantsQuery</c>.
/// </para>
/// </remarks>
public sealed record InspectAccountHomeGroupsQuery;
