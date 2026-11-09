using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Ftp;

/// <summary>
/// Holds the Ftp module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="FtpModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="FtpModule"/> is the DI entry point.
/// </summary>
public static class FtpManifest
{
    /// <summary>The Ftp module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="LicenceTier.Included"/> because FTPS is table-stakes compatibility rather than a
    /// value-add: a customer migrating from another panel arrives with an FTP client configured,
    /// and a panel that charges extra for the protocol they already use has priced the migration.
    /// </para>
    /// <para>
    /// <see cref="AgentCapability.Ftps"/> and NOTHING else, which an administrator reads before
    /// installing and <c>AgentCapabilityGuard</c> refuses to compose past. In particular NOT
    /// <see cref="AgentCapability.Firewall"/>: this module does not open the passive port range, it
    /// reports which range an operator would have to open. And NOT
    /// <see cref="AgentCapability.Ssl"/>: nothing here writes certificate material — the daemon
    /// calls only READ whether material exists, and the reload only restarts a daemon so it picks
    /// up material the Ssl module put there.
    /// </para>
    /// <para>
    /// <see cref="ModuleKind.Packageable"/>: FTPS is optional compatibility, not part of the core.
    /// <see cref="Manifest.SchemaName"/>
    /// is the <c>ftp</c> schema. Its menu entry is the second child of the "File transfer" entry the Sftp module
    /// declares, so it is not a top-level door.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "ftp",
        DisplayNameKey: "FtpModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "ftp",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Ftps],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "file-transfer-ftps",
                RouteName: "file-transfer-ftps",
                IconKey: ModuleIconKeys.FolderKey,
                LabelKey: "FtpMenuFileTransferFtps",
                Order: 10,
                Audience: ModuleAudience.Everyone),
        ],
        Routes: [
            new ModuleRoute(
                Name: "file-transfer-ftps",
                Path: "/file-transfer/ftps",
                TitleKey: "FtpRouteFileTransferFtps",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
