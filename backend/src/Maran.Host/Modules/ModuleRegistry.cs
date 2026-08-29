using Maran.Modules.Accounts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>Explicit registry of compiled-in modules (plans 2+ add entries).</summary>
public static class ModuleRegistry
{
    /// <summary>All modules in load order. Deliberately explicit — no assembly scanning.</summary>
    public static IReadOnlyList<IPanelModule> All { get; } = [new AccountsModule()];
}
