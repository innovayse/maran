namespace Maran.Sdk.Contracts;

/// <summary>
/// Whether a module is part of the panel itself or something an administrator can install from a
/// package and remove again.
/// </summary>
/// <remarks>
/// The test is the one rules/architecture.md already applies to a shared facility: who would still
/// need the module if it were removed. A module the rest of the panel cannot run without is
/// <see cref="Core"/>; one that can be taken away without breaking a sibling is
/// <see cref="Packageable"/>. The distinction is a declaration read by tests and by the loader, never
/// a switch a module flips at run time, and it is orthogonal to <see cref="LicenceTier"/>: a core
/// module may be sold as an add-on, and a packageable one may be bundled.
/// </remarks>
public enum ModuleKind
{
    /// <summary>
    /// Ships inside the panel and is never installed from, or replaced by, a package. A core module
    /// may not depend on a packageable one, because removing the latter would then break the former.
    /// </summary>
    Core,

    /// <summary>
    /// Can be installed, upgraded and removed as a package. It may depend on core modules and on
    /// other packageable ones.
    /// </summary>
    Packageable,
}
