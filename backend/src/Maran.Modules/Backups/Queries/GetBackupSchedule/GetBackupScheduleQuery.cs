namespace Maran.Modules.Backups.Queries.GetBackupSchedule;

/// <summary>Reads one backup schedule: the host-wide policy, or one account's override.</summary>
/// <remarks>
/// There is deliberately no "list every schedule" query beside this one. The settings screen edits
/// one schedule at a time — the host policy, or the override of the account it is looking at — and a
/// listing would be a second read shape with no screen behind it (rules/architecture.md, YAGNI).
/// </remarks>
/// <param name="AccountId">
/// The account whose override to read, or <c>null</c> for the host-wide policy. Bound from the query
/// string; which accounts a caller may ask about is the controller's administrator policy, not this
/// query's business.
/// </param>
public sealed record GetBackupScheduleQuery(Guid? AccountId);
