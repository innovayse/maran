using Maran.Sdk.Resources;

namespace Maran.Sdk.Modules;

/// <summary>
/// The coded reasons a package or an installed module is turned away at composition, as ready-made
/// <see cref="Error"/> values. The Sdk owns them because the codes live in its <c>ErrorMessages</c> table,
/// whose generated class is internal, and because the composition root and a separately distributed
/// loader must spell the same refusal the same way.
/// </summary>
/// <remarks>
/// Each code is a key of <c>Resources/ErrorMessages.resx</c> (en, ru, hy), so a refusal renders as a
/// sentence in the administrator's language and never carries a path, a type name or exception text.
/// </remarks>
public static class ModuleRefusals
{
    /// <summary>The package declares a module that ships with the panel, which cannot come from a package.</summary>
    public static readonly Error NotPackageable =
        Error.Of(nameof(ErrorMessages.ModuleNotPackageable), ErrorType.Validation);

    /// <summary>A module with the same id is already part of the panel.</summary>
    public static readonly Error IdTaken =
        Error.Of(nameof(ErrorMessages.ModuleIdTaken), ErrorType.Conflict);

    /// <summary>The package carries an assembly the panel reserves, or one a composed module already uses.</summary>
    public static readonly Error AssemblyReserved =
        Error.Of(nameof(ErrorMessages.ModuleAssemblyReserved), ErrorType.Validation);

    /// <summary>The module's assembly could not be read.</summary>
    public static readonly Error AssemblyUnreadable =
        Error.Of(nameof(ErrorMessages.ModuleAssemblyUnreadable), ErrorType.Validation);

    /// <summary>The module's manifest could not be read, or lacks a member the contract requires.</summary>
    public static readonly Error ManifestUnreadable =
        Error.Of(nameof(ErrorMessages.ModuleManifestUnreadable), ErrorType.Validation);

    /// <summary>The module uses a part of the agent its manifest does not declare.</summary>
    public static readonly Error AgentCapabilityUndeclared =
        Error.Of(nameof(ErrorMessages.ModuleAgentCapabilityUndeclared), ErrorType.Validation);

    /// <summary>The module's manifest breaks the module contract.</summary>
    public static readonly Error ManifestInvalid =
        Error.Of(nameof(ErrorMessages.ModuleManifestInvalid), ErrorType.Validation);
}
