namespace Maran.Modules.Databases.Common;

/// <summary>A repairing pass: the rows it rewrote (<c>kind</c> is <c>applied</c>).</summary>
/// <param name="Grants">Rows that were rewritten.</param>
public sealed record GrantRepairAppliedDto(IReadOnlyList<RepairedGrantDto> Grants) : GrantRepairWorkDto;
