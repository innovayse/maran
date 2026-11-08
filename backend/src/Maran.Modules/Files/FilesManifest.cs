using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Files;

/// <summary>
/// Holds the Files module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept distinct from <see cref="FilesModule"/>
/// because <see cref="Manifest"/> is plain data with no framework dependency.
/// </summary>
public static class FilesManifest
{
    /// <summary>The Files module's published identity.</summary>
    /// <remarks>
    /// The module declares exactly one part of the agent it may drive, the files service, and owns no
    /// schema: a customer's files and the trash beside them live on the host.
    /// <para>
    /// <see cref="ModuleKind.Core"/>: the file manager is how a customer reaches the files every other
    /// feature produces.
    /// <see cref="Manifest.SchemaName"/> is null: a customer's files live on the host, not in a table.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "files",
        DisplayNameKey: "FilesModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: null,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Files],
        Permissions: [AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "file-manager",
                RouteName: "file-manager",
                IconKey: ModuleIconKeys.FolderKey,
                LabelKey: "FilesMenuFileManager",
                Order: 10,
                Audience: ModuleAudience.Everyone),
        ],
        Routes: [
            new ModuleRoute(
                Name: "file-manager",
                Path: "/files",
                TitleKey: "FilesRouteFileManager",
                RequiresModule: null),
            new ModuleRoute(
                Name: "file-editor",
                Path: "/files/edit",
                TitleKey: "FilesRouteFileEditor",
                RequiresModule: null),
            new ModuleRoute(
                Name: "file-trash",
                Path: "/files/trash",
                TitleKey: "FilesRouteFileTrash",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
