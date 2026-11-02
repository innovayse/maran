using System.Text.Json.Serialization;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// A census of every hosting account's home directory group: what one pass read and what it refused
/// to change. Exactly one of two shapes, told apart by <c>kind</c>, so the screen never has to guess
/// which kind of pass it is showing and never reads a list that is empty by construction: an
/// inspection carries the homes it WOULD re-group, a repair carries the homes it DID.
/// </summary>
/// <remarks>
/// <para>
/// A reader who cannot tell an inspection from an action would not know whether their customers'
/// homes had already been re-grouped, and a repairing pass over a host with nothing to repair
/// produces the same empty lists a report-only pass does — so the case is stated by the caller that
/// ran the pass and carried in the type, never inferred from which list is empty.
/// </para>
/// <para>
/// <b>What no screen rendering this may claim.</b> A repaired home says nothing about whether a real
/// web server can now traverse it — the agent's own predicate proves only that the directory's group
/// column now matches (docs/superpowers/notes/2026-09-19-home-group-repair-threat-note.md). Nothing
/// here should be read as "this site now serves"; only as "this home's group is now correct".
/// </para>
/// <para>
/// The primary constructor of an abstract record is protected; the two shapes below are the whole
/// set this module declares.
/// </para>
/// </remarks>
/// <param name="Examined">
/// Hosting accounts read and classified. Every account lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the two list lengths of the shape equals this number.
/// </param>
/// <param name="AlreadyCorrect">
/// Accounts whose home was already group-owned by the web server's group. A clean, already-repaired
/// host reports every account here, and that outcome is a successful answer rather than an empty one.
/// </param>
/// <param name="Refused">Accounts left untouched, each with its reason and its advice.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HomeGroupInspectionReportDto), "inspection")]
[JsonDerivedType(typeof(HomeGroupRepairedReportDto), "repaired")]
public abstract record HomeGroupRepairReportDto(
    uint Examined,
    uint AlreadyCorrect,
    IReadOnlyList<RefusedHomeDto> Refused);
