namespace Maran.Modules.Accounts.Common;

/// <summary>The homes a repair re-grouped on the host (<c>kind</c> is <c>applied</c>).</summary>
/// <param name="Homes">The homes that were re-grouped; empty when the pass found nothing to change.</param>
public sealed record HomeGroupRepairAppliedDto(IReadOnlyList<RepairedHomeDto> Homes) : HomeGroupRepairWorkDto;
