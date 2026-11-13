using System.Resources;
using Maran.Modules.Licensing.Domain.Interfaces;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Extensions;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
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
/// <b>What this module offers the rest of the panel.</b> One read-only window,
/// <see cref="ILicenceEntitlements"/> (<see cref="Services.LicenceEntitlements"/>), that answers "does
/// the installed licence, and where one is needed its lease, cover this module?" from the single rule
/// <see cref="Domain.Policies.ModuleCoveragePolicy"/>. The request-time gate in the Sdk, the module
/// catalogue and a paid module's own jobs all ask that window, so the menu and the boundary cannot
/// disagree. The licence screen's two endpoints stay administrator-only and sit behind no gate: no
/// licence failure can lock the administrator out of installing one.
/// </para>
/// <para>
/// <b>The module is in the <c>ModuleRegistry</c>.</b> It has a screen (Settings, Licence), so the
/// promise the registry makes to the SPA's composition check can be kept, and its query and command
/// are dispatched through the bus like every other module's. The start-up probe
/// (<see cref="Services.LicenceStartupCheck"/>) is still registered by the Host and still calls the
/// verifier directly, so the "core never dies" path shares no code with the window the gate relies on.
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
        // The production key is whatever the release embedded, or none: no key is ever a constant
        // in source, so no build can trust a key whose private half is public.
        services.AddSingleton(_ => { return EmbeddedLicenceVerificationKeySource.Read(); });
        services.AddSingleton<ILicenceSignatureVerifier, Ed25519LicenceSignatureVerifier>();
        // Reads this host's machine-id through the agent, for licences that name one server. A
        // singleton like the verifier that consumes it, and like IAgentMonitorClient underneath —
        // no lifetime is narrowed anywhere along that chain, which is what a singleton verifier
        // requires to be resolvable at all.
        services.AddSingleton<IServerIdentitySource, AgentServerIdentitySource>();
        services.AddSingleton<LicenceVerifier>();

        // The lease: a different key from the licence's (a leaked lease key must not mint licences),
        // embedded by the release or absent, never a constant in source. A build with no lease key
        // refuses every lease and a licence that needs one lapses - the safe direction.
        services.AddSingleton(_ => { return EmbeddedLeaseVerificationKeySource.Read(); });
        services.AddSingleton<ILeaseSignatureVerifier, Ed25519LeaseSignatureVerifier>();
        services.AddOptions<LeaseStorageOptions>()
            .Bind(configuration.GetSection(LeaseStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Reads and verifies lease.json; whatever writes that file is not part of this module's seam.
        // Registered as the Sdk's interface so the closed side can substitute its own source. One
        // instance answers both seams, so the verified reading and the signed bytes handed to the licence
        // service come from the same class (the module's own ILeaseEnvelopeSource is internal to it).
        services.AddSingleton<FileLeaseSource>();
        services.AddSingleton<ILicenceLeaseSource>(provider =>
        {
            return provider.GetRequiredService<FileLeaseSource>();
        });
        services.AddSingleton<ILeaseEnvelopeSource>(provider =>
        {
            return provider.GetRequiredService<FileLeaseSource>();
        });

        // The daily renewal of the lease: the one thing in this module that writes lease.json and the one that
        // leaves the server (rules/security.md item 10, fifth exception). The reader judges an answer the way
        // the file source judges the stored file; the writer is the atomic one; the renewer is the pass; the
        // worker is the schedule (the Host registers it as the hosted service, a module may not); the alerter
        // is scoped because it carries the journal. The options are validated at start for the deadline only:
        // the address is checked when a request is made, so a typo can never stop the panel booting.
        services.AddOptions<LeaseServiceOptions>()
            .Bind(configuration.GetSection(LeaseServiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddLeaseServiceClient();
        // Asking for a lease now (an install does) is the same pass the schedule runs: the requester is the
        // module's worker singleton, so the two can never be two objects with two runs of failures.
        services.AddSingleton<ILeaseRenewalRequester>(provider =>
        {
            return provider.GetRequiredService<LeaseRenewalWorker>();
        });
        services.AddSingleton<LeaseEnvelopeReader>();
        services.AddSingleton<ILeaseWriter, FileLeaseWriter>();
        services.AddSingleton<LeaseRenewer>();
        services.AddSingleton<LeaseRenewalWorker>();
        services.AddScoped<LeaseRenewalAlerter>();

        // Where the licence screen sends an administrator to claim a licence for this server: an address the
        // administrator's browser opens, never one the panel contacts. Checked when the link is built, so a typo
        // can only cost the link.
        services.AddOptions<LicenceClaimOptions>().Bind(configuration.GetSection(LicenceClaimOptions.SectionName));

        // How early the installed licence's expiry is warned about. The thresholds are validated at start, because
        // one that silently never fires is worse than a refusal to boot; the source holds nothing and reads the
        // clock on every call, the alerter is scoped because it carries the journal.
        services.AddOptions<LicenceExpiryWarningOptions>()
            .Bind(configuration.GetSection(LicenceExpiryWarningOptions.SectionName))
            .Validate(
                options => { return LicenceExpiryWarningPolicy.AreValid(options.ExpiryWarningDays); },
                "Licensing:ExpiryWarningDays must hold whole days between 1 and 365, or be empty to turn the warning off.")
            .ValidateOnStart();
        services.AddSingleton<LicenceExpiryWarningSource>();
        // What the operator was last told about the expiry survives a restart, as a file beside the licence.
        services.AddSingleton<ILicenceExpiryAnnouncementStore, FileLicenceExpiryAnnouncementStore>();
        services.AddScoped<LicenceExpiryAlerter>();
        services.AddSingleton<LicenceServerFactsSource>();

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

        // The licence window. Singletons all the way down because the gate asks on every paid request:
        // the source holds nothing, the cache is the shared memory (a scoped one would remember
        // nothing between requests), and the window logs a failure once per outage. The invalidator is
        // the cache itself, so the install command and the window can never disagree about which
        // remembered answer is stale.
        services.AddSingleton<LicenceCoverageSource>();
        services.AddSingleton<LicenceCoverageCache>();
        services.AddSingleton<ILicenceCoverageInvalidator>(provider =>
        {
            return provider.GetRequiredService<LicenceCoverageCache>();
        });
        services.AddSingleton<ILicenceEntitlements, LicenceEntitlements>();

        // The lease as the bearer credential of the licence-bound services. Registered after the Sdk's
        // TryAdd of NoLeaseRequestAuthorizer or before it: either way this registration is the one the
        // marketplace resolves (the Sdk only adds its stand-in when nothing else is registered, and a
        // later registration wins over an earlier one). A singleton, because it holds nothing and reads
        // through the shared coverage cache.
        services.AddSingleton<ILeaseRequestAuthorizer, LeaseRequestAuthorizer>();

        // The pass that journals a lapse or a recovery from outside any request. A singleton because it
        // remembers the last verdict per module; the Host registers it as the hosted service (a module
        // may not), through this registration so a test drives the same instance.
        services.AddSingleton<LicenceCoverageWatcher>();

        // Scoped, because it carries LicenceStatusDisplayNames: the line `maran status` prints.
        services.AddScoped<LicenceStatusLine>();

        // Scoped, like the Databases module's GrantRepairRefusalDisplayNames: it resolves against
        // IStringLocalizer<DisplayNames>, which is culture-sensitive per request.
        services.AddScoped<LicenceStatusDisplayNames>();

        // Scoped, like the Databases module's DatabaseAuditJournal: it carries ICurrentUser, which is
        // per-request.
        services.AddScoped<LicensingAuditJournal>();

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
