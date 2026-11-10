using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>Restates what an update check found for one installed module as the wire shape.</summary>
public static class ModuleUpdateMapper
{
    /// <summary>Maps one finding.</summary>
    /// <param name="notice">What the check found.</param>
    /// <param name="installedVersion">The installed version.</param>
    /// <param name="refusal">Why the update cannot start now, decided by <c>InstallEligibility</c>; null when it can.</param>
    /// <param name="refusalText">The refusal in the caller's language; null when there is none.</param>
    /// <returns>The wire shape.</returns>
    public static ModuleUpdateDto ToDto(
        ModuleUpdateNotice notice,
        string installedVersion,
        Error? refusal,
        string? refusalText)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return new ModuleUpdateDto(
            notice.ModuleId,
            notice.ModuleId,
            installedVersion,
            notice.Latest?.Version,
            notice.Revoked,
            notice.RevokedReason,
            refusal is null,
            refusalText);
    }
}
