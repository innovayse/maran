namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>
/// What an administrator asked of one module. A closed set so the audit journal maps each to its own
/// action constant and no caller can hand it an action name of its own.
/// </summary>
public enum MarketplaceOperation
{
    /// <summary>Install a module that is not installed.</summary>
    Install,

    /// <summary>Replace an installed module with a newer version.</summary>
    Update,

    /// <summary>Remove an installed module; its data is kept.</summary>
    Remove,

    /// <summary>Return an installed module to the version it ran before its last update.</summary>
    Rollback,
}
