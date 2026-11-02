namespace Maran.Modules.Accounts.Common;

/// <summary>
/// A pass that changed nothing on the host (<c>kind</c> is <c>inspection</c>): the census plus the
/// homes a repair would re-group.
/// </summary>
/// <param name="Examined">Hosting accounts read and classified.</param>
/// <param name="AlreadyCorrect">Accounts whose home was already group-owned by the web server's group.</param>
/// <param name="Refused">Accounts left untouched, each with its reason and its advice.</param>
/// <param name="WouldRepair">Homes a repair would re-group; empty when nothing needs it.</param>
public sealed record HomeGroupInspectionReportDto(
    uint Examined,
    uint AlreadyCorrect,
    IReadOnlyList<RefusedHomeDto> Refused,
    IReadOnlyList<RepairedHomeDto> WouldRepair) : HomeGroupRepairReportDto(Examined, AlreadyCorrect, Refused);
