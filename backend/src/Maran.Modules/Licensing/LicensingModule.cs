using System.Resources;
using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Queries.GetLicenceStatus;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing;

/// <summary>
/// The Licensing module's entry point (rules/csharp.md "Canonical backend layout"). Registers the
/// offline licence verifier (<see cref="LicenceVerifier"/>, spec §228) and the operator-facing text
/// for its three-state outcome, and nothing that reads a real licence file yet — see the remarks
/// below for what this slice deliberately does not add.
/// </summary>
/// <remarks>
/// <para>
/// <b>How §229 ("ядро не умирает никогда" — the core never dies) is held structurally at THIS
/// boundary, not merely at <see cref="LicenceVerifier"/>'s own.</b> <see cref="ConfigureServices"/>
/// below does exactly one thing with every type it registers: adds it to the container. It never
/// constructs a <see cref="LicenceVerifier"/> and calls <c>VerifyAsync</c> — composition-time code
/// runs while <c>WebApplication.Build()</c> is assembling the container, before any exception
/// handler, health check or logging pipeline exists to catch or report a failure there, so ANY code
/// that runs during <see cref="ConfigureServices"/> is on the one path this panel cannot recover
/// from if it throws. Verification is deferred to a <c>Microsoft.Extensions.Hosting.IHostedService</c>
/// that only the Host composition root may register (the same split
/// <c>BackgroundWorkExtensions</c> and <c>SeedingExtensions</c> already use, for the identical
/// reason stated in their own remarks: "a module may reference only the Sdk and the SharedKernel").
/// That hosted service — not this file — is where the actual §229 guarantee is enforced and proven;
/// see its own remarks and the Host-level tests named there.
/// </para>
/// <para>
/// <b>What a later slice added on top of this one.</b> <c>Controllers.LicensingController</c> now
/// reads <see cref="LicenceStatus"/> over one administrator-only <c>GET</c>, and
/// <see cref="Services.LicensingAuditJournal"/> records that the read happened. Still deliberately
/// absent: no EF persistence or migration exists for a stored licence row, which is why
/// <see cref="ILicenceRawTextSource"/>'s only implementation here,
/// <see cref="NoLicenceInstalledTextSource"/>, always answers "nothing installed"; no write endpoint
/// installs or replaces a licence; and the closed <c>PluginLoader</c> seam that would actually gate a
/// paid module's assembly load is untouched. Each is a later slice's work.
/// </para>
/// <para>
/// <b>This module is deliberately NOT registered in <c>Maran.Sdk.Extensions.ModuleRegistry</c>.</b>
/// A module in that list is a promise to the SPA's own composition check that a sidebar destination
/// exists for it, and licensing has no screen — see that file's own comment on this module by name.
/// </para>
/// </remarks>
public sealed class LicensingModule : IPanelModule
{
    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Licensing.Resources.DisplayNames";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Licensing.Resources.ErrorMessages";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return LicensingManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Stateless and offline (see LicenceVerifier's own remarks): safe as a singleton, and
        // registering it here does not evaluate it — see this type's own remarks for why that
        // distinction is the whole point.
        services.AddSingleton<ILicenceSignatureVerifier, Ed25519LicenceSignatureVerifier>();
        services.AddSingleton<LicenceVerifier>();

        // Where the installed licence artefact lives (default: /var/lib/maran/licence.json — the
        // threat note's §1 own conclusion). A server's own value is never set; the seam exists so a
        // test can point this at an isolated temp directory instead — see LicenceStorageOptions's
        // own remarks.
        services.AddOptions<LicenceStorageOptions>()
            .Bind(configuration.GetSection(LicenceStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // FileLicenceRawTextSource, not NoLicenceInstalledTextSource: the install endpoint this
        // slice adds needs something the panel can read back afterward. Stateless and does its own
        // I/O per call — safe as a singleton, same as LicenceVerifier.
        services.AddSingleton<ILicenceRawTextSource, FileLicenceRawTextSource>();

        // Installs the licence artefact via a single atomic rename() (threat note §2). Stateless —
        // safe as a singleton.
        services.AddSingleton<ILicenceWriter, FileLicenceWriter>();

        // The module's one in-process gate serializing the verify-then-write sequence across
        // concurrent install requests (threat note §4) — MUST be a singleton: a scoped registration
        // would hand every request its own semaphore and serialize nothing.
        services.AddSingleton<LicenceInstallLock>();

        // Scoped, like the Databases module's GrantRepairRefusalDisplayNames: it resolves against
        // IStringLocalizer<DisplayNames>, which is culture-sensitive per request.
        services.AddScoped<LicenceStatusDisplayNames>();

        // Scoped, like the Databases module's DatabaseAuditJournal: it carries ICurrentUser, which is
        // per-request.
        services.AddScoped<LicensingAuditJournal>();

        // Resolved directly by LicensingController, not dispatched through Wolverine — see that
        // controller's own remarks for why this module's one handler is registered here instead of
        // relying on Wolverine's assembly-scanning discovery.
        services.AddScoped<GetLicenceStatusQueryHandler>();

        // Resolved directly by LicensingController, for the identical reason GetLicenceStatusQueryHandler
        // is — this module is not in Wolverine's assembly-scanning discovery.
        services.AddScoped<InstallLicenceCommandHandler>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves
        // Manifest.DisplayNameKey against, the same registration every module makes for its own
        // DisplayNames.resx.
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(LicensingModule).Assembly));

        // The shared resource pool ResxErrorTextProvider resolves InstallLicenceCommandHandler's
        // and its validator's error codes against, the same registration every other module makes
        // for its own ErrorMessages.resx.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(LicensingModule).Assembly));
    }
}
