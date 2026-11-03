namespace Maran.Modules.Monitoring.Common;

/// <summary>One package the host's package manager reports as having a newer version available.</summary>
/// <param name="PackageName">The operating system's name for the package.</param>
/// <param name="AvailableVersion">The version the update would install.</param>
/// <param name="InstalledVersion">
/// The version installed now, or <c>null</c> when the host's package manager does not report it (the
/// RHEL family's update check does not). Null and never an empty string, so the interface draws
/// "not reported" rather than a blank that reads like a version.
/// </param>
/// <param name="Source">
/// The repository or origin the update comes from, or an empty string when the package manager names
/// none.
/// </param>
public sealed record PendingUpdateDto(
    string PackageName,
    string AvailableVersion,
    string? InstalledVersion,
    string Source);
