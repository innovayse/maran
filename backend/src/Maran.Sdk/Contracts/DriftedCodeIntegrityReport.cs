namespace Maran.Sdk.Contracts;

/// <summary>
/// At least one file the hash list names does not match what is installed. Reported as a FACT —
/// "these files differ from release X" — never as an accusation: BSL explicitly permits a customer to
/// modify their own installation, and a customer exercising that right must never read this as a claim
/// that they are running compromised or pirated software.
/// </summary>
/// <param name="InstalledVersion">The release version the comparison believes is installed.</param>
/// <param name="DifferingPaths">
/// The paths (relative to the install root, matching the hash list's own keys) that did not match. A
/// correct comparison never reports a drift with nothing named; a receiver that is handed an empty
/// list has been handed an internally inconsistent report and must not interpret it charitably as
/// clean.
/// </param>
/// <param name="ObservedAt">When the closed PluginLoader performed this comparison, by its own clock.</param>
public sealed record DriftedCodeIntegrityReport(
    string InstalledVersion,
    IReadOnlyList<string> DifferingPaths,
    DateTimeOffset ObservedAt) : CodeIntegrityReport(InstalledVersion, ObservedAt);
