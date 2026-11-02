namespace Maran.Modules.Databases.Common;

/// <summary>A report-only pass: the rows it would rewrite, with nothing on the server changed (<c>kind</c> is <c>preview</c>).</summary>
/// <param name="Grants">Rows a repair would rewrite.</param>
public sealed record GrantRepairPreviewDto(IReadOnlyList<RepairedGrantDto> Grants) : GrantRepairWorkDto;
