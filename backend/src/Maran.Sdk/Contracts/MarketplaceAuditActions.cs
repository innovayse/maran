namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for what happens to a module that arrives as a package
/// (issues #184, #188). Constants rather than an enum: the closed loader that implements
/// <see cref="Interfaces.IModuleLoader"/> may record steps this assembly was never compiled knowing
/// about, and an enum could not be extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes. A refusal is an action like any other and is
/// recorded with <c>succeeded = false</c>: the package that was turned away is the entry worth
/// reading. Every constant here needs a display name in the three languages
/// (<c>AuditActionDisplayNameTests</c>).
/// </remarks>
public static class MarketplaceAuditActions
{
    /// <summary>A package was verified and kept; the module activates at the next start.</summary>
    public const string ModuleInstalled = "ModuleInstalled";

    /// <summary>A newer package replaced an installed one; the new version activates at the next start.</summary>
    public const string ModuleUpgraded = "ModuleUpgraded";

    /// <summary>An installed module was returned to the version it ran before its last upgrade.</summary>
    public const string ModuleRolledBack = "ModuleRolledBack";

    /// <summary>A module was stopped and its package removed.</summary>
    public const string ModuleUnloaded = "ModuleUnloaded";

    /// <summary>
    /// A module the licence no longer covers stopped serving. Its data was kept and nothing was
    /// unloaded or deleted.
    /// </summary>
    public const string ModuleDegraded = "ModuleDegraded";

    /// <summary>A package or an installed module was turned away; the reason code is in the subject.</summary>
    public const string ModuleRefused = "ModuleRefused";

    /// <summary>The panel was asked to restart so that a staged module can activate.</summary>
    public const string ModuleRestartRequested = "ModuleRestartRequested";
}
