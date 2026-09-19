namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// A census of every hosting account's home directory group, as one repair pass read it.
/// </summary>
/// <param name="Examined">
/// How many hosting accounts were read and classified. Every account lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the three list counts equals this number — which is what
/// makes the report a census rather than a list of what happened to be interesting.
/// </param>
/// <param name="AlreadyCorrect">
/// Accounts whose home was already group-owned by the web server's group. Nothing was sent for
/// these. A clean, already-repaired host reports every account here.
/// </param>
/// <param name="Repaired">Homes that were re-grouped. Empty on a report-only pass.</param>
/// <param name="WouldRepair">Homes a report-only pass would have re-grouped. Empty otherwise.</param>
/// <param name="Refused">Accounts left exactly as found, each with the reason it was left.</param>
/// <remarks>
/// <b>What this report does not say.</b> Nothing here proves a real web server can traverse the
/// repaired home — the agent's own predicate proves only that the directory's group column now
/// matches the web server's group
/// (docs/superpowers/notes/2026-09-19-home-group-repair-threat-note.md). Nothing that renders this
/// type may add a sentence that implies otherwise.
/// </remarks>
public sealed record HomeGroupRepairReportDto(
    uint Examined,
    uint AlreadyCorrect,
    IReadOnlyList<RepairedHomeDto> Repaired,
    IReadOnlyList<RepairedHomeDto> WouldRepair,
    IReadOnlyList<RefusedHomeDto> Refused);
