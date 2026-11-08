using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Packaging.Models;

/// <summary>
/// The contents of <c>ipm.json</c>: the module's published identity (everything the Sdk's
/// <see cref="Manifest"/> declares, so an administrator reads what the module reaches before
/// installing) and the pins that tie every payload byte to the signature. The signature is over the file
/// as stored, so there is no canonical form: the library's writer emits the record's property order and
/// the package builder script emits sorted keys, and the reader accepts either because it parses only
/// after the signature over the stored bytes has held.
/// </summary>
/// <param name="FormatVersion">The format revision; only <c>IpmLayout.CurrentFormatVersion</c> is read.</param>
/// <param name="Id">Module id; see <see cref="Manifest.Id"/>.</param>
/// <param name="DisplayNameKey">Resx key of the module's name; see <see cref="Manifest.DisplayNameKey"/>.</param>
/// <param name="Version">This build's semantic version.</param>
/// <param name="Tier">The licence tier the module is sold under.</param>
/// <param name="Audience">Who the module's screens are for.</param>
/// <param name="MinPanelVersion">The oldest panel the module works with.</param>
/// <param name="MaxPanelVersion">The newest panel the module is known to work with, or null for no ceiling.</param>
/// <param name="NotBefore">The instant before which the package is not valid, or null for none.</param>
/// <param name="SchemaName">The schema the module owns (equal to the id), or null when it owns none.</param>
/// <param name="AgentCapabilities">The areas of the agent the module may drive.</param>
/// <param name="Permissions">The authorization policy names its controllers use.</param>
/// <param name="Menu">The navigation entries it contributes.</param>
/// <param name="Routes">The screens it registers.</param>
/// <param name="Dependencies">The modules it requires, each with an accepted version range.</param>
/// <param name="Migrations">The migrations it carries; present exactly when <paramref name="SchemaName"/> is.</param>
/// <param name="EntryAssembly">The file name of the assembly holding the module, such as <c>Maran.Samples.Hello.dll</c>.</param>
/// <param name="Spa">The interface bundle, or null for a module with no screen of its own.</param>
/// <param name="Encryption">The encrypted-library reservation, or null for a plain package.</param>
/// <param name="Files">Every payload file with its hash and size; no other entry may exist.</param>
public sealed record IpmManifest(
    int FormatVersion,
    string Id,
    string DisplayNameKey,
    string Version,
    LicenceTier Tier,
    ModuleAudience Audience,
    string MinPanelVersion,
    string? MaxPanelVersion,
    DateTimeOffset? NotBefore,
    string? SchemaName,
    IReadOnlyList<AgentCapability> AgentCapabilities,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ModuleMenuItem> Menu,
    IReadOnlyList<ModuleRoute> Routes,
    IReadOnlyList<ModuleDependency> Dependencies,
    IpmMigrationRange? Migrations,
    string EntryAssembly,
    IpmSpaInfo? Spa,
    IpmEncryption? Encryption,
    IReadOnlyList<IpmFileEntry> Files)
{
    /// <summary>
    /// The Sdk manifest this package declares. Always <see cref="ModuleKind.Packageable"/>: a package
    /// cannot be core, and the format has no field to claim otherwise.
    /// </summary>
    /// <returns>The manifest the Sdk's rules judge.</returns>
    public Manifest ToManifest()
    {
        return new Manifest(
            Id,
            DisplayNameKey,
            Version,
            Tier,
            ModuleKind.Packageable,
            MinPanelVersion,
            SchemaName,
            Dependencies,
            AgentCapabilities,
            Permissions,
            Menu,
            Routes,
            Audience);
    }
}
