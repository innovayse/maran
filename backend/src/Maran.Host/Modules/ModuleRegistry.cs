using Maran.Modules.Accounts;
using Maran.Modules.Identity;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>Explicit registry of compiled-in modules (plans 2+ add entries).</summary>
public static class ModuleRegistry
{
    /// <summary>
    /// All modules in load order. Deliberately explicit — no assembly scanning. Identity comes
    /// first: it owns who may sign in, so every other module's endpoints are meaningless until
    /// its services are registered.
    /// </summary>
    public static IReadOnlyList<IPanelModule> All { get; } = [new IdentityModule(), new AccountsModule()];
}
