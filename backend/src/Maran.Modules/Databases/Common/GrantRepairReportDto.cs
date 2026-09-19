namespace Maran.Modules.Databases.Common;

/// <summary>
/// A census of this server's database-level grant table: what one repair pass read, what it changed,
/// and what it refused to change.
/// </summary>
/// <param name="IsReportOnly">
/// True when nothing on the server was changed. The field exists so the screen can never have to
/// guess which kind of pass it is showing: "would repair" and "repaired" are separate lists, and a
/// reader who cannot tell an inspection from an action would not know whether their customers' access
/// had already been rewritten.
/// </param>
/// <param name="ExaminedGrants">
/// Rows read and classified. Every row lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the three list lengths equals this number.
/// </param>
/// <param name="AlreadyCorrect">
/// Rows that were already right, or whose name cannot hold a wildcard at all. A clean installation
/// reports every row here, and that outcome is a successful answer rather than an empty one.
/// </param>
/// <param name="Repaired">Rows that were rewritten. Empty on a report-only pass.</param>
/// <param name="WouldRepair">Rows a report-only pass would rewrite. Empty otherwise.</param>
/// <param name="Refused">Rows left untouched, each with its reason and its advice.</param>
/// <remarks>
/// <b>What no screen rendering this may claim.</b> A repaired row says nothing about what was read
/// while it was wrong; after a pass the host is no longer exposed going forward, and it is NOT "clean
/// of consequences". An empty exposure list on a repaired row does not clear that row either
/// (docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md). Whether the reach was exercised is
/// answerable only from the database server's query log, which is off on a default install.
/// </remarks>
public sealed record GrantRepairReportDto(
    bool IsReportOnly,
    uint ExaminedGrants,
    uint AlreadyCorrect,
    IReadOnlyList<RepairedGrantDto> Repaired,
    IReadOnlyList<RepairedGrantDto> WouldRepair,
    IReadOnlyList<RefusedGrantDto> Refused);
