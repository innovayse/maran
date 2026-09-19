namespace Maran.Modules.Accounts.Common;

/// <summary>
/// A census of every hosting account's home directory group: what one repair pass read, what it
/// changed, and what it refused to change.
/// </summary>
/// <param name="IsReportOnly">
/// True when nothing on the host was changed. The field exists so the screen can never have to guess
/// which kind of pass it is showing: "would repair" and "repaired" are separate lists, and a reader who
/// cannot tell an inspection from an action would not know whether their customers' homes had already
/// been re-grouped.
/// </param>
/// <param name="Examined">
/// Hosting accounts read and classified. Every account lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the three list lengths equals this number.
/// </param>
/// <param name="AlreadyCorrect">
/// Accounts whose home was already group-owned by the web server's group. A clean, already-repaired
/// host reports every account here, and that outcome is a successful answer rather than an empty one.
/// </param>
/// <param name="Repaired">Homes that were re-grouped. Empty on a report-only pass.</param>
/// <param name="WouldRepair">Homes a report-only pass would re-group. Empty otherwise.</param>
/// <param name="Refused">Accounts left untouched, each with its reason and its advice.</param>
/// <remarks>
/// <b>What no screen rendering this may claim.</b> A repaired home says nothing about whether a real
/// web server can now traverse it — the agent's own predicate proves only that the directory's group
/// column now matches (docs/superpowers/notes/2026-09-19-home-group-repair-threat-note.md). Nothing
/// here should be read as "this site now serves"; only as "this home's group is now correct".
/// </remarks>
public sealed record HomeGroupRepairReportDto(
    bool IsReportOnly,
    uint Examined,
    uint AlreadyCorrect,
    IReadOnlyList<RepairedHomeDto> Repaired,
    IReadOnlyList<RepairedHomeDto> WouldRepair,
    IReadOnlyList<RefusedHomeDto> Refused);
