using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Queries.ListInstalled;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Commands.InstallModule;

/// <summary>
/// Installing a module is accepted as a task, refused with a coded reason before anything is reserved, or
/// stopped by the loader without touching what runs. Every refusal is paired with an accepting control, and
/// every action and every refusal leaves an audit entry.
/// </summary>
public sealed class InstallModuleCommandHandlerTests
{
    /// <summary>The audit action every install request is journalled under.</summary>
    private const string Action = MarketplaceClientAuditActions.ModuleInstallRequested;

    /// <summary>Builds a world offering <c>mail</c> at the given versions, the newest first.</summary>
    /// <param name="versions">The versions on offer.</param>
    /// <returns>The world.</returns>
    private static OperationWorld OfferingMail(params string[] versions)
    {
        var world = new OperationWorld();
        var offered = versions.Select(version => { return CatalogueSamples.Version(version); }).ToArray();
        world.Offer(CatalogueSamples.Module("mail", offered[0]), offered);
        return world;
    }

    /// <summary>Asserts that exactly one refused entry names the given reason.</summary>
    /// <param name="world">The world whose audit entries are read.</param>
    /// <param name="reason">The reason code the entry must carry.</param>
    private static void AssertRefusalAudited(OperationWorld world, string reason)
    {
        var entry = Assert.Single(world.AuditFor(Action));
        Assert.False(entry.Succeeded);
        Assert.Contains($"reason={reason}", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>An install is accepted before any work is done, then stages the package and leaves the module installed, not active, waiting for a restart.</summary>
    [Fact]
    public async Task Install_stages_the_package_and_asks_for_a_restart()
    {
        using var world = OfferingMail("1.2.0");

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        var task = Assert.Single(world.Tasks.Begun);
        Assert.Equal(TaskKinds.ModuleInstall, task.Kind);
        Assert.Equal(TaskAudience.AdministratorOnly, task.Audience);
        Assert.Equal(accepted.Value.TaskId, task.Id);
        Assert.Equal(0, world.Loader.StageAttempts);

        await world.RunDispatchedAsync();

        Assert.Equal(
            new[]
            {
                ModuleOperationRunner.CheckingEntitlementLine,
                ModuleOperationRunner.DownloadingLine,
                ModuleOperationRunner.VerifyingSignatureLine,
                ModuleOperationRunner.StagingLine,
                ModuleOperationRunner.DoneLine,
            },
            world.Tasks.Lines.Select(line => { return line.Line; }));
        Assert.Equal(accepted.Value.TaskId, Assert.Single(world.Tasks.Completed));
        Assert.Empty(world.Tasks.Failed);

        var listed = await new ListInstalledQueryHandler(world.Installed, world.Names)
            .HandleAsync(new ListInstalledQuery(), CancellationToken.None);
        var module = Assert.Single(listed.Value);
        Assert.Equal(ModuleLifecycleState.Installed, module.Lifecycle);
        Assert.NotEqual(ModuleLifecycleState.Active, module.Lifecycle);
        Assert.True(module.RestartRequired);

        var row = await world.Ledger.FindCurrentAsync("mail", CancellationToken.None);
        Assert.Equal(InstalledPackageState.Staged, row!.State);
        Assert.All(world.Client.DownloadedFiles, path => { Assert.False(File.Exists(path)); });
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>A package whose own manifest disagrees with what the catalogue announced is refused by the loader and nothing is installed; the same package agreeing is accepted.</summary>
    [Fact]
    public async Task A_package_whose_manifest_disagrees_with_the_catalogue_is_refused()
    {
        using var world = OfferingMail("1.2.0");
        world.Loader.ClaimedVersion = "9.9.9";

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.True(accepted.IsSuccess);
        var failure = Assert.Single(world.Tasks.Failed);
        Assert.Equal("ModulePackageNotAsAnnounced", failure.Code);
        Assert.Empty(world.Tasks.Completed);
        Assert.Empty(await world.Loader.ListAsync(CancellationToken.None));
        Assert.Null(await world.Ledger.FindCurrentAsync("mail", CancellationToken.None));
        Assert.Contains(world.AuditFor(Action), entry => { return !entry.Succeeded && entry.Subject.Contains("ModulePackageNotAsAnnounced", StringComparison.Ordinal); });
    }

    /// <summary>The accepting control: a package whose manifest agrees with the catalogue is installed.</summary>
    [Fact]
    public async Task A_package_whose_manifest_agrees_with_the_catalogue_is_accepted()
    {
        using var world = OfferingMail("1.2.0");
        world.Loader.ClaimedModuleId = "mail";
        world.Loader.ClaimedVersion = "1.2.0";

        await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.Single(world.Tasks.Completed);
        Assert.Empty(world.Tasks.Failed);
        Assert.Single(await world.Loader.ListAsync(CancellationToken.None));
    }

    /// <summary>A version the service withdrew is refused as revoked and nothing is reserved.</summary>
    [Fact]
    public async Task A_revoked_version_is_refused_and_a_live_one_is_accepted()
    {
        using var world = new OperationWorld();
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.2.0")),
            CatalogueSamples.Version("1.2.0"),
            CatalogueSamples.Version("1.1.0", revoked: true));

        var refused = await world.InstallHandler().HandleAsync(world.Install("mail", "1.1.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.PackageRevoked, refused.Error!.Code);
        Assert.Empty(world.Tasks.Begun);
        AssertRefusalAudited(world, "PackageRevoked");

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>A version that needs a newer panel than this one is refused; one that runs on this panel is accepted.</summary>
    [Fact]
    public async Task An_incompatible_panel_version_is_not_installable()
    {
        using var world = new OperationWorld();
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("2.0.0", minPanelVersion: "2.0.0")),
            CatalogueSamples.Version("2.0.0", minPanelVersion: "2.0.0"),
            CatalogueSamples.Version("1.2.0", minPanelVersion: "1.0.0"));

        var refused = await world.InstallHandler().HandleAsync(world.Install("mail", "2.0.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.IncompatiblePanel, refused.Error!.Code);
        AssertRefusalAudited(world, "IncompatiblePanel");

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>
    /// A retry that reaches a restarted panel while the first install is still open is answered with the
    /// running task's id and creates nothing, because the open operation is stored on the module's claim and
    /// not remembered in process memory. A different version is still the conflict, so the answer is not
    /// simply "anything goes through".
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_restart_is_answered_with_the_running_task()
    {
        using var world = OfferingMail("1.2.0", "1.1.0");
        var first = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        var retried = await world.InstallHandlerAfterRestart()
            .HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.True(retried.IsSuccess);
        Assert.Equal(first.Value.TaskId, retried.Value.TaskId);
        Assert.Single(world.Tasks.Begun);
        Assert.Single(world.Dispatcher.Dispatched);
        Assert.Single(world.AuditFor(Action));

        var other = await world.InstallHandlerAfterRestart()
            .HandleAsync(world.Install("mail", "1.1.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleOperationInProgress, other.Error!.Code);
        Assert.Single(world.Tasks.Begun);
    }

    /// <summary>The catalogue's name for the module is kept with the staged package, and the installed list shows it.</summary>
    [Fact]
    public async Task The_installed_list_shows_the_catalogue_name_the_install_kept()
    {
        using var world = new OperationWorld();
        var version = CatalogueSamples.Version("1.2.0");
        world.Offer(CatalogueSamples.Module("mail", version) with { DisplayName = "Mail server" }, version);

        await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        await world.RunDispatchedAsync();
        world.Loader.Seed("mail", "1.2.0", ModuleLifecycleState.Installed);

        var listed = await new ListInstalledQueryHandler(world.Installed, world.Names)
            .HandleAsync(new ListInstalledQuery(), CancellationToken.None);

        var mail = Assert.Single(listed.Value);
        Assert.Equal("Mail server", mail.DisplayName);
        Assert.Equal("mail", mail.Id);
    }

    /// <summary>A second operation on a module another one holds is a conflict that reserves nothing; a different module proceeds.</summary>
    [Fact]
    public async Task A_concurrent_operation_on_the_same_module_is_a_conflict()
    {
        using var world = OfferingMail("1.2.0");
        world.Offer(CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0")), CatalogueSamples.Version("1.0.0"));
        await world.Gate.AcquireAsync("mail", CancellationToken.None);

        var refused = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleOperationInProgress, refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Tasks.Begun);
        Assert.Empty(world.Dispatcher.Dispatched);
        AssertRefusalAudited(world, "ModuleOperationInProgress");

        var other = await world.InstallHandler().HandleAsync(world.Install("dns", "1.0.0"), CancellationToken.None);
        Assert.True(other.IsSuccess);
    }

    /// <summary>Asking for the same install again while it runs answers with the same task and creates no second one; once it finished, asking again is already installed.</summary>
    [Fact]
    public async Task Installing_the_same_version_twice_creates_one_task()
    {
        using var world = OfferingMail("1.2.0");

        var first = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        var second = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.TaskId, second.Value.TaskId);
        Assert.Single(world.Tasks.Begun);
        Assert.Single(world.Dispatcher.Dispatched);
        Assert.Single(world.AuditFor(Action));

        await world.RunDispatchedAsync();
        var third = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleAlreadyInstalled, third.Error!.Code);
        Assert.Single(world.Tasks.Begun);
    }

    /// <summary>An unreachable marketplace refuses the install, and a download that fails mid-run fails the task; either way the installed modules are untouched.</summary>
    [Fact]
    public async Task An_outage_leaves_installed_modules_untouched()
    {
        using var world = OfferingMail("1.2.0");
        world.Loader.Seed("dns", "3.0.0", ModuleLifecycleState.Active);
        world.Client.Failure = MarketplaceClientRefusals.Unreachable;

        var refused = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.MarketplaceUnreachable, refused.Error!.Code);
        AssertRefusalAudited(world, "MarketplaceUnreachable");

        world.Client.Failure = null;
        world.Client.DownloadFailure = MarketplaceClientRefusals.Unreachable;
        await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.Equal(ErrorCodes.MarketplaceUnreachable, Assert.Single(world.Tasks.Failed).Code);
        Assert.Equal(0, world.Loader.StageAttempts);
        var running = Assert.Single(await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal("dns", running.ModuleId);
        Assert.Equal(ModuleLifecycleState.Active, running.State);
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>A version below the highest this panel ever installed is refused; a newer one is accepted.</summary>
    [Fact]
    public async Task The_ledger_floor_blocks_an_older_catalogue_version()
    {
        using var world = OfferingMail("2.1.0", "1.5.0");
        await world.Ledger.RecordStagedAsync("mail", "2.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        await world.Ledger.RecordRemovalAsync("mail", CancellationToken.None);

        var refused = await world.InstallHandler().HandleAsync(world.Install("mail", "1.5.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleVersionNotInstallable, refused.Error!.Code);
        AssertRefusalAudited(world, "ModuleVersionNotInstallable");

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "2.1.0"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>An install without the password is refused with the re-authentication failure and reserves nothing; with the password it is accepted.</summary>
    [Fact]
    public async Task Install_without_the_password_is_refused()
    {
        using var world = OfferingMail("1.2.0");

        var refused = await world.InstallHandler().HandleAsync(
            world.Install("mail", "1.2.0") with { Password = string.Empty }, CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", refused.Error!.Code);
        Assert.Equal(ErrorType.Validation, refused.Error.Type);
        Assert.Empty(world.Tasks.Begun);
        Assert.Empty(world.Dispatcher.Dispatched);
        Assert.Equal(0, world.Client.ReadCalls);
        AssertRefusalAudited(world, "ReauthenticationFailed");

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>A wrong password is refused, reaches the password check once so it counts toward lockout, and is audited.</summary>
    [Fact]
    public async Task A_wrong_password_is_refused_and_counts()
    {
        using var world = OfferingMail("1.2.0");

        var refused = await world.InstallHandler().HandleAsync(
            world.Install("mail", "1.2.0") with { Password = "guess" }, CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", refused.Error!.Code);
        Assert.Equal(1, world.Reauthentication.Refusals);
        AssertRefusalAudited(world, "ReauthenticationFailed");
    }

    /// <summary>An install the administrator did not acknowledge is refused before the password is even checked; an acknowledged one is accepted.</summary>
    [Fact]
    public async Task An_unacknowledged_install_is_refused()
    {
        using var world = OfferingMail("1.2.0");

        var refused = await world.InstallHandler().HandleAsync(
            world.Install("mail", "1.2.0") with { Acknowledged = false }, CancellationToken.None);

        Assert.Equal(ErrorCodes.InstallNotAcknowledged, refused.Error!.Code);
        Assert.Equal(0, world.Reauthentication.Calls);
        Assert.Empty(world.Tasks.Begun);
        AssertRefusalAudited(world, "InstallNotAcknowledged");

        var accepted = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>Without a lease the service cannot state an entitlement, so install is refused; an entitled module is offered.</summary>
    [Fact]
    public async Task Without_a_lease_install_is_refused_and_an_entitled_module_is_accepted()
    {
        using var world = new OperationWorld();
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.2.0"), EntitlementState.Unknown),
            CatalogueSamples.Version("1.2.0"));
        world.Offer(
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0"), EntitlementState.NotPurchased),
            CatalogueSamples.Version("1.0.0"));
        world.Offer(
            CatalogueSamples.Module("ftp", CatalogueSamples.Version("1.0.0"), EntitlementState.Entitled),
            CatalogueSamples.Version("1.0.0"));

        var browseOnly = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        var unpurchased = await world.InstallHandler().HandleAsync(world.Install("dns", "1.0.0"), CancellationToken.None);
        var entitled = await world.InstallHandler().HandleAsync(world.Install("ftp", "1.0.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.LeaseRequired, browseOnly.Error!.Code);
        Assert.Equal(ErrorCodes.EntitlementMissing, unpurchased.Error!.Code);
        Assert.True(entitled.IsSuccess);
        Assert.Equal(2, world.AuditFor(Action).Count(entry => { return !entry.Succeeded; }));
        Assert.Single(world.AuditFor(Action), entry => { return entry.Succeeded; });
    }

    /// <summary>A module the catalogue does not list is not found, journalled, and starts nothing.</summary>
    [Fact]
    public async Task A_module_the_catalogue_does_not_list_is_refused()
    {
        using var world = OfferingMail("1.2.0");

        var refused = await world.InstallHandler().HandleAsync(world.Install("ghost", "1.0.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInCatalogue, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        AssertRefusalAudited(world, "ModuleNotInCatalogue");
    }

    /// <summary>A switched-off marketplace refuses the install with its own word and makes no download.</summary>
    [Fact]
    public async Task A_switched_off_marketplace_refuses_the_install()
    {
        using var world = OfferingMail("1.2.0");
        await world.Settings.DisableAsync(world.UserId, CancellationToken.None);

        var refused = await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.MarketplaceDisabled, refused.Error!.Code);
        Assert.Equal(0, world.Client.DownloadCalls);
        AssertRefusalAudited(world, "MarketplaceDisabled");
    }

    /// <summary>When the bus cannot take the work the task is failed, the gate is released and the exception continues.</summary>
    [Fact]
    public async Task A_broken_bus_fails_the_task_and_frees_the_module()
    {
        using var world = OfferingMail("1.2.0");
        world.Dispatcher.Throws = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await world.InstallHandler().HandleAsync(world.Install("mail", "1.2.0"), CancellationToken.None);
        });

        Assert.Equal(ErrorCodes.ModuleOperationFailed, Assert.Single(world.Tasks.Failed).Code);
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }
}
