namespace Maran.Sdk.Contracts;

/// <summary>
/// Every file the comparison could name matched the signed hash list for <see cref="CodeIntegrityReport.InstalledVersion"/>.
/// A statement of fact about the files the release shipped — it says nothing about files a customer
/// added, which this mechanism cannot see (see <see cref="CodeIntegrityReport"/>'s own remarks).
/// </summary>
/// <param name="InstalledVersion">The release version the comparison believes is installed.</param>
/// <param name="ObservedAt">When the closed PluginLoader performed this comparison, by its own clock.</param>
public sealed record CleanCodeIntegrityReport(string InstalledVersion, DateTimeOffset ObservedAt)
    : CodeIntegrityReport(InstalledVersion, ObservedAt);
