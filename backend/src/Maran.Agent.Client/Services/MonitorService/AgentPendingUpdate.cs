namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>One OS package the host's package manager reports as upgradable (issue #324).</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>PendingUpdate</c>, so callers outside this project never hold
/// a generated protobuf type. The listing is read from the package manager's local cache and never
/// refreshed, so a row says what the cache last learned, not what a mirror offers right now.
/// </remarks>
/// <param name="PackageName">The OS package name.</param>
/// <param name="AvailableVersion">The version the update installs.</param>
/// <param name="InstalledVersion">
/// The version installed now, or <c>null</c> when the host's package manager does not report it
/// (the wire's empty string, read once at the client boundary).
/// </param>
/// <param name="Source">The apt origin or dnf repository id the update comes from; empty when none was reported.</param>
public sealed record AgentPendingUpdate(
    string PackageName,
    string AvailableVersion,
    string? InstalledVersion,
    string Source);
