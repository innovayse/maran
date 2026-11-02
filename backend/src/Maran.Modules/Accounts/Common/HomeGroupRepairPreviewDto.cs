namespace Maran.Modules.Accounts.Common;

/// <summary>The homes a repair would re-group; nothing on the host was changed (<c>kind</c> is <c>preview</c>).</summary>
/// <param name="Homes">The homes that need re-grouping; empty when nothing does.</param>
public sealed record HomeGroupRepairPreviewDto(IReadOnlyList<RepairedHomeDto> Homes) : HomeGroupRepairWorkDto;
