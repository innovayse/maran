using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Packaging.Models;

/// <summary>What a package is judged against: the panel it would run in and, optionally, the installation.</summary>
/// <param name="PanelVersion">The version of the running panel.</param>
/// <param name="Now">The current instant, supplied by the caller so verification reads no ambient clock.</param>
/// <param name="InstalledModules">
/// The installed modules, id to version, or null when the package is being checked on its own (the
/// command-line verb and a third-party author's check have no installation). With null, the
/// installation-dependent rules (a dependency that must be installed, a downgrade) are not judged.
/// </param>
/// <param name="Downgrade">The operator's decision to go back to an older version, or null.</param>
public sealed record IpmVerificationContext(
    SemanticVersion PanelVersion,
    DateTimeOffset Now,
    IReadOnlyDictionary<string, string>? InstalledModules,
    IpmDowngradeOverride? Downgrade);
