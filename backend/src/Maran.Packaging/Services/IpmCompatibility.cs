using Maran.Packaging.Constants;
using Maran.Packaging.Models;
using Maran.Packaging.Resources;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Packaging.Services;

/// <summary>
/// Whether a package fits the panel it would run in and the installation it would join, as pure
/// functions: the same inputs always give the same answer, nothing is read from the machine, and the
/// clock is a parameter. The module rules of the manifest itself are the Sdk's; this is only the
/// comparison with the world outside the package.
/// </summary>
public static class IpmCompatibility
{
    /// <summary>Whether this library reads packages of a format revision.</summary>
    /// <param name="formatVersion">The manifest's <c>formatVersion</c>.</param>
    /// <returns>Success for a revision this library reads; otherwise the refusal.</returns>
    public static Result CheckFormat(int formatVersion)
    {
        return formatVersion == IpmLayout.CurrentFormatVersion
            ? Result.Ok()
            : Result.Fail(Error.Of(nameof(ErrorMessages.IpmFormatVersionUnknown), ErrorType.Validation));
    }

    /// <summary>
    /// Whether the package is valid now and for this panel's newest side. The oldest side
    /// (<c>minPanelVersion</c>) is the Sdk's manifest rule and is reported by
    /// <see cref="IpmManifestValidator"/>, so there is one place that says a panel is too old.
    /// </summary>
    /// <param name="manifest">The package's manifest.</param>
    /// <param name="panelVersion">The running panel's version.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>Success, or the refusal: the panel is newer than the package supports, or the package is not valid yet.</returns>
    public static Result CheckPanel(IpmManifest manifest, SemanticVersion panelVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(panelVersion);

        if (manifest.MaxPanelVersion is not null
            && (!SemanticVersion.TryParse(manifest.MaxPanelVersion, out var ceiling)
                || SemanticVersion.Compare(panelVersion, ceiling!) > 0))
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.IpmPanelTooNew), ErrorType.Validation));
        }

        if (manifest.NotBefore is not null && manifest.NotBefore > now)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.IpmNotYetValid), ErrorType.Validation));
        }

        return Result.Ok();
    }

    /// <summary>
    /// Whether the package may join this installation: no older version than the installed one without
    /// an explicit, reasoned override, and every dependency installed in an accepted version.
    /// </summary>
    /// <param name="manifest">The package's manifest.</param>
    /// <param name="installed">The installed modules, id to version.</param>
    /// <param name="downgrade">The operator's decision to go back, or null.</param>
    /// <returns>Success, or the refusal.</returns>
    public static Result CheckInstall(
        IpmManifest manifest,
        IReadOnlyDictionary<string, string> installed,
        IpmDowngradeOverride? downgrade)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(installed);

        if (installed.TryGetValue(manifest.Id, out var current) && IsDowngrade(manifest.Version, current)
            && string.IsNullOrWhiteSpace(downgrade?.Reason))
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.IpmDowngradeRefused), ErrorType.Conflict));
        }

        foreach (var dependency in manifest.Dependencies)
        {
            if (!installed.TryGetValue(dependency.Id, out var version) || !Accepts(dependency.Range, version))
            {
                return Result.Fail(Error.Of(nameof(ErrorMessages.IpmDependencyUnsatisfied), ErrorType.Conflict));
            }
        }

        return Result.Ok();
    }

    /// <summary>True when the package is older than the installed version, or either cannot be read (fail closed).</summary>
    /// <param name="packageVersion">The package's version.</param>
    /// <param name="installedVersion">The installed version.</param>
    /// <returns>True when going to the package is a step back or cannot be shown to be a step forward.</returns>
    private static bool IsDowngrade(string packageVersion, string installedVersion)
    {
        if (!SemanticVersion.TryParse(packageVersion, out var candidate)
            || !SemanticVersion.TryParse(installedVersion, out var existing))
        {
            return true;
        }

        return SemanticVersion.Compare(candidate!, existing!) < 0;
    }

    /// <summary>True when an installed version is inside a dependency's range.</summary>
    /// <param name="range">The dependency's range.</param>
    /// <param name="version">The installed version.</param>
    /// <returns>True when both read and the version is accepted.</returns>
    private static bool Accepts(string range, string version)
    {
        return VersionRange.TryParse(range, out var accepted)
            && SemanticVersion.TryParse(version, out var parsed)
            && accepted!.Contains(parsed!);
    }
}
