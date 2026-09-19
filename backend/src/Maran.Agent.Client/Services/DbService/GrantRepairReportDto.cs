namespace Maran.Agent.Client.Services.DbService;

/// <summary>
/// A census of this server's database-level grant table, as one repair pass read it.
/// </summary>
/// <param name="ExaminedGrants">
/// How many rows were read and classified. Every row lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the three list counts equals this number — which is what
/// makes the report a census rather than a list of what happened to be interesting.
/// </param>
/// <param name="AlreadyCorrect">
/// Rows that already held the escaped form, or whose name cannot hold a wildcard at all. Nothing was
/// sent for these. A clean installation reports every row here.
/// </param>
/// <param name="Repaired">Rows that were rewritten. Empty on a report-only pass.</param>
/// <param name="WouldRepair">Rows a report-only pass would have rewritten. Empty otherwise.</param>
/// <param name="Refused">Rows left exactly as found, each with the reason it was left.</param>
/// <remarks>
/// <b>What this report does not say.</b> A repaired row proves nothing about whether the defect was
/// ever used; the host is not "clean of consequences" after a pass, only no longer exposed going
/// forward (docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md). Nothing that renders this
/// type may add a sentence that implies otherwise.
/// </remarks>
public sealed record GrantRepairReportDto(
    uint ExaminedGrants,
    uint AlreadyCorrect,
    IReadOnlyList<RepairedGrantDto> Repaired,
    IReadOnlyList<RepairedGrantDto> WouldRepair,
    IReadOnlyList<RefusedGrantDto> Refused);
