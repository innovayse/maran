using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The refusals the module's own commands and eligibility rules raise, each stated once with the kind of
/// failure it is. The refusals of the marketplace client are in <see cref="MarketplaceClientRefusals"/>.
/// </summary>
public static class MarketplaceOperationRefusals
{
    /// <summary>The administrator did not confirm they read what the module may do.</summary>
    public static readonly Error NotAcknowledged =
        Error.Of(nameof(ErrorMessages.InstallNotAcknowledged), ErrorType.Validation);

    /// <summary>The module is not installed.</summary>
    public static readonly Error ModuleNotInstalled =
        Error.Of(nameof(ErrorMessages.ModuleNotInstalled), ErrorType.NotFound);

    /// <summary>The module is already installed, at this version or, for an install, at any version.</summary>
    public static readonly Error ModuleAlreadyInstalled =
        Error.Of(nameof(ErrorMessages.ModuleAlreadyInstalled), ErrorType.Conflict);

    /// <summary>The version is not on offer, or is older than one this panel installed before.</summary>
    public static readonly Error VersionNotInstallable =
        Error.Of(nameof(ErrorMessages.ModuleVersionNotInstallable), ErrorType.Conflict);

    /// <summary>Another operation is running on the module.</summary>
    public static readonly Error OperationInProgress =
        Error.Of(nameof(ErrorMessages.ModuleOperationInProgress), ErrorType.Conflict);

    /// <summary>The module has no earlier version to go back to.</summary>
    public static readonly Error NoPreviousVersion =
        Error.Of(nameof(ErrorMessages.ModuleHasNoPreviousVersion), ErrorType.Conflict);

    /// <summary>The operation stopped before it finished, for a reason the panel does not name to the caller.</summary>
    public static readonly Error OperationFailed =
        Error.Of(nameof(ErrorMessages.ModuleOperationFailed), ErrorType.Failure);
}
