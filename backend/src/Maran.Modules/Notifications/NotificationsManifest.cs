using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications;

/// <summary>
/// Holds the Notifications module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="NotificationsModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="NotificationsModule"/> is the DI/routing entry point.
/// </summary>
public static class NotificationsManifest
{
    /// <summary>The Notifications module's published identity.</summary>
    /// <remarks>
    /// <see cref="LicenceTier.Included"/>, and it could not be anything else: password reset is a
    /// security feature of the free panel and it needs this module to deliver its mail. A tier that
    /// could be withheld would make an unlicensed panel silently unable to reset a password.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "notifications",
        DisplayNameKey: "NotificationsModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: []);
}
