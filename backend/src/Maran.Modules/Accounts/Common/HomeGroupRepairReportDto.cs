namespace Maran.Modules.Accounts.Common;

/// <summary>
/// A census of every hosting account's home directory group: what one pass read, what it changed,
/// and what it refused to change.
/// </summary>
/// <param name="Examined">
/// Hosting accounts read and classified. Every account lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the work's homes plus the refused homes equals this number.
/// </param>
/// <param name="AlreadyCorrect">
/// Accounts whose home was already group-owned by the web server's group. A clean, already-repaired
/// host reports every account here, and that outcome is a successful answer rather than an empty one.
/// </param>
/// <param name="Work">
/// The homes that needed re-grouping, as a report lists them or as a repair re-grouped them. The shape
/// says which: a screen must never have to infer from an empty list whether the host was changed.
/// </param>
/// <param name="Refused">Accounts left untouched, each with its reason and its advice.</param>
/// <remarks>
/// <b>What no screen rendering this may claim.</b> A repaired home says nothing about whether a real
/// web server can now traverse it — the agent's own predicate proves only that the directory's group
/// column now matches (docs/superpowers/notes/2026-09-19-home-group-repair-threat-note.md). Nothing
/// here should be read as "this site now serves"; only as "this home's group is now correct".
/// </remarks>
public sealed record HomeGroupRepairReportDto(
    uint Examined,
    uint AlreadyCorrect,
    HomeGroupRepairWorkDto Work,
    IReadOnlyList<RefusedHomeDto> Refused);
