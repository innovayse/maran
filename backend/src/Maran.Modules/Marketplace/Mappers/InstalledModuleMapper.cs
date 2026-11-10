using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>Restates an installed module, with the decisions already made about it, as the wire shape.</summary>
public static class InstalledModuleMapper
{
    /// <summary>Maps one installed module.</summary>
    /// <param name="facts">The loader's answer beside the ledger's row.</param>
    /// <param name="actions">What is pending for the module and what can be done with it.</param>
    /// <param name="failureMessage">Why a degraded or failed module is in that state, in the caller's language; otherwise null.</param>
    /// <returns>The wire shape.</returns>
    public static InstalledModuleDto ToDto(InstalledModuleFacts facts, InstalledModuleActions actions, string? failureMessage)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(actions);

        var loaded = facts.Loaded;

        return new InstalledModuleDto(
            loaded.ModuleId,
            facts.DisplayName ?? loaded.ModuleId,
            loaded.Version,
            loaded.State,
            failureMessage,
            loaded.Origin,
            facts.Package?.InstalledAt,
            facts.Package?.PreviousVersion,
            actions.RestartRequired,
            actions.DataKept,
            facts.Revoked,
            actions.CanRollback,
            actions.CanRemove);
    }
}
