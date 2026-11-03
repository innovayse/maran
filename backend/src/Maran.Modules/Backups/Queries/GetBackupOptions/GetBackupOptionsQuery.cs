namespace Maran.Modules.Backups.Queries.GetBackupOptions;

/// <summary>
/// Reads the closed sets the backup screens select from: the history filters' statuses and reasons,
/// the destinations filter's kinds of storage, and the schedule form's cadences, weekdays, hours and
/// retention counts.
/// </summary>
/// <remarks>
/// Reference data (rules/architecture.md "The backend owns the data, the SPA renders it"): the same
/// for every caller, so it carries no tenant and discloses nothing about any account. The validators
/// remain the boundary — a choice list is advice.
/// </remarks>
public sealed record GetBackupOptionsQuery;
