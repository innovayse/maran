using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The ONE place the panel decides whether a module version can be installed or updated now, and where a
/// catalogue module stands. The screen shows these answers and never derives them from a tier or an
/// entitlement word, and the install and update commands ask the same questions before they start work, so
/// the button and the endpoint cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rules, in the order the first refusal wins:</b> the marketplace is switched on; the catalogue names a
/// version; neither the module nor the version is withdrawn; the version runs on this panel's version (its
/// minimum, and its maximum when it has one, by <see cref="VersionRange"/>); the licence entitles the panel
/// to the module (a missing lease reads as <c>LeaseRequired</c>, so a panel without one can browse and
/// cannot install); the version is not the one already installed; it is not older than any version this
/// panel has installed before (the ledger's floor, which a replayed older catalogue cannot lower); and no
/// other operation is running on the module.
/// </para>
/// <para>
/// A catalogue that could not be verified never reaches this type: there is no module to judge, and the
/// caller reports the verification failure instead. Making the switch the first rule keeps a switched-off
/// marketplace refusing with its own word however the caller got here.
/// </para>
/// </remarks>
public sealed class InstallEligibility
{
    /// <summary>Whether the marketplace is switched on.</summary>
    private readonly MarketplaceSettingsStore _settings;

    /// <summary>What is installed.</summary>
    private readonly InstalledModuleReader _installed;

    /// <summary>The ledger, whose floor refuses an older version.</summary>
    private readonly InstalledPackageLedger _ledger;

    /// <summary>The module's database context, read for the operation gate rows.</summary>
    private readonly MarketplaceDbContext _context;

    /// <summary>This panel's version.</summary>
    private readonly IPanelVersion _panelVersion;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the eligibility rules.</summary>
    /// <param name="settings">Whether the marketplace is switched on.</param>
    /// <param name="installed">What is installed.</param>
    /// <param name="ledger">The ledger, whose floor refuses an older version.</param>
    /// <param name="context">The module's database context, read for the operation gate rows.</param>
    /// <param name="panelVersion">This panel's version.</param>
    /// <param name="clock">The time source.</param>
    public InstallEligibility(
        MarketplaceSettingsStore settings,
        InstalledModuleReader installed,
        InstalledPackageLedger ledger,
        MarketplaceDbContext context,
        IPanelVersion panelVersion,
        IClock clock)
    {
        _settings = settings;
        _installed = installed;
        _ledger = ledger;
        _context = context;
        _panelVersion = panelVersion;
        _clock = clock;
    }

    /// <summary>Reads the panel-side facts the decisions need, once, so a page is judged against one picture.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The facts.</returns>
    public async Task<EligibilityContext> LoadContextAsync(CancellationToken ct)
    {
        var enabled = await _settings.IsEnabledAsync(ct);
        var installed = await _installed.ReadAsync(ct);

        var now = _clock.UtcNow;
        var locks = await _context.ModuleOperationLocks.AsNoTracking().ToListAsync(ct);
        var inOperation = locks
            .Where(held => { return !held.IsStale(now); })
            .Select(held => { return held.ModuleId; })
            .ToHashSet(StringComparer.Ordinal);

        return new EligibilityContext(
            enabled,
            installed.ToDictionary(facts => { return facts.Loaded.ModuleId; }, StringComparer.Ordinal),
            inOperation);
    }

    /// <summary>Decides where a catalogue module stands and whether a version of it can be staged now.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="module">The catalogue module.</param>
    /// <param name="version">The version to judge; the module's newest for the catalogue list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The state, and the refusal when the version cannot be staged.</returns>
    public async Task<ModuleAssessment> AssessAsync(
        EligibilityContext context,
        CatalogueModule module,
        CatalogueVersion? version,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        var refusal = await RefusalAsync(context, module.Id, module.Revoked, module.Entitlement.State, version, ct);
        return new ModuleAssessment(StateOf(context, module, version), refusal);
    }

    /// <summary>Decides whether a version can be installed: the offer rules, and the module must not be installed yet.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="module">The catalogue module.</param>
    /// <param name="version">The version asked for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refusal, or null when the install may start.</returns>
    public async Task<Error?> RefuseInstallAsync(
        EligibilityContext context,
        CatalogueModule module,
        CatalogueVersion? version,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        if (IsInstalled(context, module.Id))
        {
            return MarketplaceOperationRefusals.ModuleAlreadyInstalled;
        }

        return await RefusalAsync(context, module.Id, module.Revoked, module.Entitlement.State, version, ct);
    }

    /// <summary>Decides whether a version can be applied as an update: the offer rules, and the module must be installed.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="module">The catalogue module.</param>
    /// <param name="version">The version asked for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refusal, or null when the update may start.</returns>
    public async Task<Error?> RefuseUpdateAsync(
        EligibilityContext context,
        CatalogueModule module,
        CatalogueVersion? version,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(module);

        if (!IsInstalled(context, module.Id))
        {
            return MarketplaceOperationRefusals.ModuleNotInstalled;
        }

        return await RefusalAsync(context, module.Id, module.Revoked, module.Entitlement.State, version, ct);
    }

    /// <summary>
    /// Decides whether the update an update check announced can be applied: the update rules, except the
    /// entitlement, which the announcement does not carry and the service decides when the download is
    /// granted.
    /// </summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="notice">What the check found for an installed module.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refusal, or null when the update may start.</returns>
    public async Task<Error?> RefuseUpdateOfAsync(
        EligibilityContext context,
        ModuleUpdateNotice notice,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notice);

        if (!IsInstalled(context, notice.ModuleId))
        {
            return MarketplaceOperationRefusals.ModuleNotInstalled;
        }

        return await RefusalAsync(context, notice.ModuleId, notice.Revoked, entitlement: null, notice.Latest, ct);
    }

    /// <summary>Tells whether a module is installed and not removed.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="moduleId">The module id.</param>
    /// <returns>True when the loader lists it in any state but removed.</returns>
    public static bool IsInstalled(EligibilityContext context, string moduleId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Installed.TryGetValue(moduleId, out var facts)
            && facts.Loaded.State != ModuleLifecycleState.Removed;
    }

    /// <summary>The first rule the version breaks, in the order the class documents.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="moduleId">The module id.</param>
    /// <param name="moduleRevoked">Whether the service withdrew the whole module.</param>
    /// <param name="entitlement">The service's entitlement state, or null when the answer being judged carries none.</param>
    /// <param name="version">The version to judge.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refusal, or null when none is broken.</returns>
    private async Task<Error?> RefusalAsync(
        EligibilityContext context,
        string moduleId,
        bool moduleRevoked,
        EntitlementState? entitlement,
        CatalogueVersion? version,
        CancellationToken ct)
    {
        if (!context.MarketplaceEnabled)
        {
            return MarketplaceClientRefusals.Disabled;
        }

        var offered = Parse(version?.Version);
        if (version is null || offered is null)
        {
            return MarketplaceOperationRefusals.VersionNotInstallable;
        }

        if (moduleRevoked || version.Revoked)
        {
            return MarketplaceClientRefusals.PackageRevoked;
        }

        if (!RunsOnThisPanel(version))
        {
            return MarketplaceClientRefusals.IncompatiblePanel;
        }

        var unentitled = entitlement is null ? null : EntitlementRefusal(entitlement.Value);
        if (unentitled is not null)
        {
            return unentitled;
        }

        if (context.Installed.TryGetValue(moduleId, out var installed)
            && installed.Loaded.State != ModuleLifecycleState.Removed
            && Parse(installed.Loaded.Version) is { } running
            && SemanticVersion.Compare(running, offered) == 0)
        {
            return MarketplaceOperationRefusals.ModuleAlreadyInstalled;
        }

        var floor = await _ledger.ReadFloorAsync(moduleId, ct);
        if (floor is not null && SemanticVersion.Compare(offered, floor) < 0)
        {
            return MarketplaceOperationRefusals.VersionNotInstallable;
        }

        return context.ModulesInOperation.Contains(moduleId) ? MarketplaceOperationRefusals.OperationInProgress : null;
    }

    /// <summary>Where the module stands on this panel.</summary>
    /// <param name="context">The panel-side facts.</param>
    /// <param name="module">The catalogue module.</param>
    /// <param name="version">The version judged.</param>
    /// <returns>The state.</returns>
    private MarketplaceModuleState StateOf(EligibilityContext context, CatalogueModule module, CatalogueVersion? version)
    {
        if (module.Revoked || version?.Revoked == true)
        {
            return MarketplaceModuleState.Revoked;
        }

        if (version is not null && !RunsOnThisPanel(version))
        {
            return MarketplaceModuleState.Incompatible;
        }

        if (!IsInstalled(context, module.Id))
        {
            return MarketplaceModuleState.Available;
        }

        var running = Parse(context.Installed[module.Id].Loaded.Version);
        var offered = Parse(version?.Version);

        return running is not null && offered is not null && SemanticVersion.Compare(running, offered) < 0
            ? MarketplaceModuleState.UpdateAvailable
            : MarketplaceModuleState.Installed;
    }

    /// <summary>The refusal an entitlement state gives, or null when it entitles the panel.</summary>
    /// <param name="state">The service's entitlement state.</param>
    /// <returns>The refusal, or null.</returns>
    private static Error? EntitlementRefusal(EntitlementState state)
    {
        return state switch
        {
            EntitlementState.NotRequired => null,
            EntitlementState.Entitled => null,
            EntitlementState.NotPurchased => MarketplaceClientRefusals.EntitlementMissing,
            EntitlementState.LeaseExpired => MarketplaceClientRefusals.LeaseRequired,
            _ => MarketplaceClientRefusals.LeaseRequired,
        };
    }

    /// <summary>Tells whether a version runs on this panel's version.</summary>
    /// <param name="version">The catalogue version, whose minimum and optional maximum are read.</param>
    /// <returns>True when the panel's version is inside the range; false when it is outside or either side is unreadable.</returns>
    private bool RunsOnThisPanel(CatalogueVersion version)
    {
        var panel = Parse(_panelVersion.Current);
        if (panel is null)
        {
            return false;
        }

        var text = version.MaxPanelVersion is null
            ? $">={version.MinPanelVersion}"
            : $">={version.MinPanelVersion} <={version.MaxPanelVersion}";

        return VersionRange.TryParse(text, out var range) && range is not null && range.Contains(panel);
    }

    /// <summary>Parses a version.</summary>
    /// <param name="text">The text, or null.</param>
    /// <returns>The version, or null when the text is not a valid semantic version.</returns>
    private static SemanticVersion? Parse(string? text)
    {
        return SemanticVersion.TryParse(text, out var version) ? version : null;
    }
}
