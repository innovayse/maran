namespace Maran.Modules.Backups.Common;

/// <summary>One entry of a choice list the backup screens select from.</summary>
/// <param name="Value">
/// The machine-stable value, which is what the screen sends back or filters by: a status, a reason,
/// a kind of storage, a cadence, a weekday name, an hour or a number of copies. The endpoints that
/// receive it validate it again, so a list is advice and never the boundary.
/// </param>
/// <param name="Label">The choice as an operator reads it, resolved for the request's culture.</param>
public sealed record BackupChoiceDto(string Value, string Label);
