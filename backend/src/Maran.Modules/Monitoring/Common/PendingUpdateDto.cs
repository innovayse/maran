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
/// <param name="Classification">
/// What the agent measured about the update: <c>security</c> when the package manager marks it as a
/// security fix, <c>ordinary</c> when it was measured and is not, or <c>unmeasured</c> when the agent
/// did not say. Never a guess: an update nobody classified is <c>unmeasured</c>, not <c>ordinary</c>.
/// </param>
/// <param name="ClassificationName">
/// The classification as an administrator reads it, resolved for the request's culture, so a table
/// badge needs no lookup of its own.
/// </param>
public sealed record PendingUpdateDto(
    string PackageName,
    string AvailableVersion,
    string? InstalledVersion,
    string Source,
    string Classification,
    string ClassificationName);
