using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The one place a module version's eligibility is decided: each rule refuses on its own with its own code,
/// the first broken rule wins, and each refusal is paired with the same input made acceptable.
/// </summary>
public sealed class InstallEligibilityTests
{
    /// <summary>Judges a module's newest version in a world.</summary>
    /// <param name="world">The world.</param>
    /// <param name="module">The module.</param>
    /// <returns>The refusal code, or null when the version may be staged.</returns>
    private static async Task<string?> RefusalOfAsync(OperationWorld world, CatalogueModule module)
    {
        var context = await world.Eligibility.LoadContextAsync(CancellationToken.None);
        var assessment = await world.Eligibility.AssessAsync(context, module, module.Latest, CancellationToken.None);
        return assessment.Refusal?.Code;
    }

    /// <summary>A switched-off marketplace refuses first, whatever else is true; switched on, the same module is acceptable.</summary>
    [Fact]
    public async Task A_switched_off_marketplace_refuses_before_any_other_rule()
    {
        using var world = new OperationWorld();
        var module = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0", revoked: true));
        await world.Settings.DisableAsync(world.UserId, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarketplaceDisabled, await RefusalOfAsync(world, module));

        await world.Settings.EnableAsync(world.UserId, CancellationToken.None);
        Assert.Equal(ErrorCodes.PackageRevoked, await RefusalOfAsync(world, module));
    }

    /// <summary>A module with no version on offer is not installable.</summary>
    [Fact]
    public async Task A_module_with_no_version_is_not_installable()
    {
        using var world = new OperationWorld();

        Assert.Equal(
            ErrorCodes.ModuleVersionNotInstallable,
            await RefusalOfAsync(world, CatalogueSamples.Module("mail", latest: null)));
    }

    /// <summary>A withdrawn module and a withdrawn version are both revoked, in either order of cause.</summary>
    [Fact]
    public async Task A_withdrawn_module_or_version_is_revoked()
    {
        using var world = new OperationWorld();

        Assert.Equal(
            ErrorCodes.PackageRevoked,
            await RefusalOfAsync(world, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"), revoked: true)));
        Assert.Equal(
            ErrorCodes.PackageRevoked,
            await RefusalOfAsync(world, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0", revoked: true))));
        Assert.Null(await RefusalOfAsync(world, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"))));
    }

    /// <summary>The panel version must be inside the version's range, both ends included; a version just outside either end is incompatible.</summary>
    [Theory]
    [InlineData("1.4.0", null, false)]
    [InlineData("1.5.0", null, true)]
    [InlineData("1.0.0", "1.4.0", false)]
    [InlineData("1.0.0", "1.3.9", true)]
    public async Task The_panel_version_must_be_inside_the_range(string min, string? max, bool incompatible)
    {
        using var world = new OperationWorld();
        var module = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0", min, max));

        var refusal = await RefusalOfAsync(world, module);

        Assert.Equal(incompatible ? ErrorCodes.IncompatiblePanel : null, refusal);
    }

    /// <summary>Each entitlement state maps to its own refusal, and only licensed or included modules are accepted.</summary>
    [Theory]
    [InlineData(EntitlementState.NotRequired, null)]
    [InlineData(EntitlementState.Entitled, null)]
    [InlineData(EntitlementState.NotPurchased, "EntitlementMissing")]
    [InlineData(EntitlementState.LeaseExpired, "LeaseRequired")]
    [InlineData(EntitlementState.Unknown, "LeaseRequired")]
    public async Task Each_entitlement_state_has_its_own_verdict(EntitlementState state, string? expected)
    {
        using var world = new OperationWorld();

        var refusal = await RefusalOfAsync(
            world, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"), state));

        Assert.Equal(expected, refusal);
    }

    /// <summary>The version already running is "already installed"; a newer one is an update available; an older one than ever seen is blocked by the floor.</summary>
    [Fact]
    public async Task The_installed_version_the_floor_and_a_newer_version_are_told_apart()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.2.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.2.0", ModuleLifecycleState.Active);
        var context = await world.Eligibility.LoadContextAsync(CancellationToken.None);

        var same = await world.Eligibility.AssessAsync(
            context, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.2.0")), CatalogueSamples.Version("1.2.0"), CancellationToken.None);
        var older = await world.Eligibility.AssessAsync(
            context, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.1.0")), CatalogueSamples.Version("1.1.0"), CancellationToken.None);
        var newer = await world.Eligibility.AssessAsync(
            context, CatalogueSamples.Module("mail", CatalogueSamples.Version("1.3.0")), CatalogueSamples.Version("1.3.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleAlreadyInstalled, same.Refusal!.Code);
        Assert.Equal(MarketplaceModuleState.Installed, same.State);
        Assert.Equal(ErrorCodes.ModuleVersionNotInstallable, older.Refusal!.Code);
        Assert.Null(newer.Refusal);
        Assert.Equal(MarketplaceModuleState.UpdateAvailable, newer.State);
    }

    /// <summary>A module an operation is running on is not offered; once the gate is released it is.</summary>
    [Fact]
    public async Task A_module_with_an_operation_in_progress_is_not_offered()
    {
        using var world = new OperationWorld();
        var module = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"));
        var claim = await world.Gate.AcquireAsync("mail", CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleOperationInProgress, await RefusalOfAsync(world, module));

        await world.Gate.ReleaseAsync(claim.Value, CancellationToken.None);
        Assert.Null(await RefusalOfAsync(world, module));
    }

    /// <summary>A gate left behind long ago no longer blocks: the eligibility reads it as stale, like the gate does.</summary>
    [Fact]
    public async Task A_stale_gate_does_not_block_the_offer()
    {
        using var world = new OperationWorld();
        var module = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"));
        await world.Gate.AcquireAsync("mail", CancellationToken.None);
        world.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await RefusalOfAsync(world, module));
    }

    /// <summary>The install check refuses a module that is installed; the update check refuses one that is not.</summary>
    [Fact]
    public async Task Install_needs_the_module_absent_and_update_needs_it_present()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        var context = await world.Eligibility.LoadContextAsync(CancellationToken.None);
        var installed = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.1.0"));
        var absent = CatalogueSamples.Module("dns", CatalogueSamples.Version("1.1.0"));

        var installOverInstalled = await world.Eligibility.RefuseInstallAsync(
            context, installed, installed.Latest, CancellationToken.None);
        var installFresh = await world.Eligibility.RefuseInstallAsync(
            context, absent, absent.Latest, CancellationToken.None);
        var updateAbsent = await world.Eligibility.RefuseUpdateAsync(
            context, absent, absent.Latest, CancellationToken.None);
        var updateInstalled = await world.Eligibility.RefuseUpdateAsync(
            context, installed, installed.Latest, CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleAlreadyInstalled, installOverInstalled!.Code);
        Assert.Null(installFresh);
        Assert.Equal(ErrorCodes.ModuleNotInstalled, updateAbsent!.Code);
        Assert.Null(updateInstalled);
    }

    /// <summary>A module the loader lists as removed counts as not installed, so it can be installed again.</summary>
    [Fact]
    public async Task A_removed_module_can_be_installed_again()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Removed);
        var context = await world.Eligibility.LoadContextAsync(CancellationToken.None);
        var module = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"));

        var refusal = await world.Eligibility.RefuseInstallAsync(context, module, module.Latest, CancellationToken.None);

        Assert.Null(refusal);
    }
}
