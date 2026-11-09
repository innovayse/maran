namespace Maran.ArchitectureTests;

/// <summary>
/// The ways a module can break the module contract, one value per rule so a planted violation can
/// be asserted BY NAME and a failure message says which rule it is about.
/// </summary>
/// <remarks>
/// Judged by <see cref="ModuleContractLaw"/> and <see cref="ModuleDependencyLaw"/>. The contract
/// itself is written in docs/modules/module-contract.md.
/// </remarks>
public enum ModuleContractRule
{
    /// <summary>The manifest's <c>Id</c> differs from the module's <c>IPanelModule.Name</c>.</summary>
    IdIsNotName,

    /// <summary>The id is not lower-case letters and digits, so it cannot be a PostgreSQL schema name.</summary>
    IdIsNotSchemaShaped,

    /// <summary>Two composed modules publish the same id.</summary>
    DuplicateModuleId,

    /// <summary>The module's own <c>Version</c> is not a semantic version.</summary>
    VersionIsNotSemantic,

    /// <summary><c>MinPanelVersion</c> is blank or not a semantic version.</summary>
    MinPanelVersionIsNotSemantic,

    /// <summary><c>MinPanelVersion</c> is higher than the panel this build is, so no panel could host it.</summary>
    MinPanelVersionAbovePanel,

    /// <summary><c>SchemaName</c> is neither null nor the module's id.</summary>
    SchemaNameIsNotId,

    /// <summary>A declared permission is not a policy name the Sdk defines.</summary>
    PermissionOutsideVocabulary,

    /// <summary>A controller names a policy the manifest does not declare.</summary>
    ControllerPolicyNotDeclared,

    /// <summary>Two menu entries, in one module or in two, share a key.</summary>
    DuplicateMenuKey,

    /// <summary>Two routes, in one module or in two, share a name.</summary>
    DuplicateRouteName,

    /// <summary>A menu entry points at a route name the module does not declare.</summary>
    MenuPointsAtNoRoute,

    /// <summary>A dependency names a module id no composed module publishes.</summary>
    DependencyUnknown,

    /// <summary>A module lists itself as a dependency.</summary>
    DependencyOnSelf,

    /// <summary>Dependencies lead from a module back to itself.</summary>
    DependencyCycle,

    /// <summary>A core module depends on a packageable one, so losing that package would remove the core.</summary>
    CoreDependsOnPackageable,

    /// <summary>A core module is gated by a licence tier, so losing a licence could remove it.</summary>
    CoreIsGated,
}
