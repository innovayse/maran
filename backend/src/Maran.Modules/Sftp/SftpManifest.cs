using Maran.Sdk.Contracts;

namespace Maran.Modules.Sftp;

/// <summary>
/// Holds the Sftp module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="SftpModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="SftpModule"/> is the DI/routing entry point.
/// </summary>
public static class SftpManifest
{
    /// <summary>The Sftp module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// <b>The display name deliberately does not say "SFTP", and the id deliberately still does.</b>
    /// <c>SftpModuleDisplayName</c> resolves to "File transfer" (and its Russian and Armenian
    /// equivalents), because this module's entry is the only one the panel's sidebar shows for file
    /// transfer and the screen it opens lists SFTP and FTPS logins side by side. A menu entry
    /// labelled with one of the two protocols tells a customer the other one is somewhere else.
    /// The same arrangement as <c>CronManifest</c> ("cron" / "Scheduled tasks") and
    /// <c>IdentityManifest</c> ("identity" / "Users and access"): the id is a key, the display name
    /// is a sentence to a person, and they are allowed to differ.
    /// </para>
    /// <para>
    /// <see cref="Manifest.Id"/> stays <c>sftp</c> because it is not a label: it reaches the browser
    /// as <c>ModuleDto.Name</c> and is the exact string the SPA routes and licence-gates on — a
    /// route's <c>meta.module</c>, the sidebar's landing-route map, and the upgrade page's parameter
    /// all key on it — and it is the name this module's PostgreSQL schema carries. Renaming it would
    /// be a migration plus a contract change to buy nothing a customer can see. For the same reason
    /// <see cref="Manifest.DisplayNameKey"/> keeps its <c>Sftp</c> prefix —
    /// <c>ManifestUniquenessTests.A_modules_display_name_key_names_the_module_it_belongs_to</c>
    /// requires a module's display-name key to start with its id.
    /// </para>
    /// <para>
    /// Two things a reader might expect to be tied to this id are NOT, and neither is an argument
    /// for renaming it. The schema name is its own constant (<c>SftpDbContext.SchemaName</c>), so
    /// the two spell "sftp" separately and only rules/architecture.md keeps them equal; and this
    /// module's audit rows carry the authenticated principal as their actor, never a module prefix
    /// — what says "Sftp" there is the action name, and those are <c>AuditActions</c> constants in
    /// the Sdk that no rename of this id would reach.
    /// </para>
    /// <para>
    /// What this module handles is unchanged and still SFTP only. Its error messages name SFTP on
    /// purpose: they are about SFTP logins, and the FTPS module owns its own.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "sftp",
        DisplayNameKey: "SftpModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Sftp]);
}
