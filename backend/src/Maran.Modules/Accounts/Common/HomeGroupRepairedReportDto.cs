namespace Maran.Modules.Accounts.Common;

/// <summary>
/// A pass that re-grouped homes on the host (<c>kind</c> is <c>repaired</c>): the census plus the
/// homes it actually changed.
/// </summary>
/// <param name="Examined">Hosting accounts read and classified.</param>
/// <param name="AlreadyCorrect">Accounts whose home was already group-owned by the web server's group.</param>
/// <param name="Refused">Accounts left untouched, each with its reason and its advice.</param>
/// <param name="Repaired">Homes that were re-grouped; empty when the pass found nothing to change.</param>
public sealed record HomeGroupRepairedReportDto(
    uint Examined,
    uint AlreadyCorrect,
    IReadOnlyList<RefusedHomeDto> Refused,
    IReadOnlyList<RepairedHomeDto> Repaired) : HomeGroupRepairReportDto(Examined, AlreadyCorrect, Refused);
