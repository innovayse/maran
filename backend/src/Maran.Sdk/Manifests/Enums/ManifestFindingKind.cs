namespace Maran.Sdk.Manifests.Enums;

/// <summary>
/// The ways a <see cref="Contracts.Manifest"/> can fail <see cref="ManifestValidator"/>. A closed set:
/// the loader turns each into a refusal an administrator reads, and the contract tests name the kind
/// they expect, so a new rule is a new case here and never a reused one.
/// </summary>
public enum ManifestFindingKind
{
    /// <summary>The module id is not lowercase letters and digits beginning with a letter, or is too long to be a schema name.</summary>
    IdMalformed,

    /// <summary>Two composed modules declare the same id.</summary>
    DuplicateModuleId,

    /// <summary>The module's own version is not a valid semantic version.</summary>
    VersionInvalid,

    /// <summary>The minimum panel version is not a valid semantic version.</summary>
    MinPanelVersionInvalid,

    /// <summary>The module needs a newer panel than the one running.</summary>
    MinPanelVersionAbovePanel,

    /// <summary>The module declares a schema whose name is not the module's id.</summary>
    SchemaNameNotId,

    /// <summary>The module declares a permission that is not a policy the Sdk names.</summary>
    PermissionUnknown,

    /// <summary>Two menu entries share a key, inside one module or across modules.</summary>
    DuplicateMenuKey,

    /// <summary>Two routes share a name, inside one module or across modules.</summary>
    DuplicateRouteName,

    /// <summary>A route requires a module that is not another composed module.</summary>
    RouteRequiresUnknownModule,

    /// <summary>A dependency names a module that is not composed.</summary>
    DependencyUnknown,

    /// <summary>A dependency's version range is not a valid range.</summary>
    DependencyRangeInvalid,

    /// <summary>A dependency is composed, but its version is outside the range the module asks for.</summary>
    DependencyVersionUnsatisfied,

    /// <summary>Following dependencies from the module leads back to the module.</summary>
    DependencyCycle,

    /// <summary>A core module depends on a packageable one, which removing would break.</summary>
    CoreDependsOnPackageable,
}
