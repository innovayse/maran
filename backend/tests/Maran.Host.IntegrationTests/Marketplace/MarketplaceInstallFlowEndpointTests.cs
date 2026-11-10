using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// Installing, removing and refusing a module over the panel's real HTTP surface, with a fake marketplace
/// behind the named client, the contract tests' fake module loader behind the Sdk seam and a real
/// PostgreSQL: what the panel accepts becomes a task, a ledger row and an audit entry; what it refuses
/// leaves no task, no ledger row and no request to the marketplace, and is audited too.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal is paired with the call that is accepted: the acknowledgement and the password are each
/// withheld and then given, the catalogue is signed by a stranger's key here and by the pinned key in the
/// accepted flow, the grant names another host here and the configured one in the accepted flow, and a
/// customer's call is refused where the administrator's identical call is accepted.
/// </para>
/// <para>
/// UNOBSERVED HERE: that the staged module is composed after a restart, which one running host cannot
/// cross, and that a real package's signature and manifest are verified (the loader is a fake; the closed
/// loader's own suite proves it). What is observed is everything the marketplace owns around that moment.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MarketplaceInstallFlowEndpointTests : IAsyncLifetime
{
    /// <summary>A password that is not the administrator's.</summary>
    private const string WrongPassword = "not the administrator's password";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MarketplaceInstallFlowEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// An install with the acknowledgement and the password is accepted with a task id; the task completes,
    /// the ledger shows the staged version, the installed list says "installed, restart required", the
    /// request is audited, and the lease reached the grant request but not the package download.
    /// </summary>
    [Fact]
    public async Task Installing_with_the_acknowledgement_and_the_password_stages_the_package_and_asks_for_a_restart()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);

        var accepted = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        var task = await world.SettledTaskAsync(accepted.Json().GetProperty("taskId").GetGuid());
        Assert.Equal(PanelTaskStatus.Completed, task.Status);
        Assert.Null(task.ErrorCode);
        Assert.Equal(TaskKinds.ModuleInstall, task.Kind);
        Assert.Equal(MarketplaceWorld.ModuleId, task.Subject);
        Assert.Equal(TaskAudience.AdministratorOnly, task.Audience);

        var ledger = Assert.Single(await world.LedgerAsync());
        Assert.Equal(MarketplaceWorld.ModuleId, ledger.ModuleId);
        Assert.Equal(MarketplaceWorld.ModuleVersion, ledger.Version);
        Assert.Equal(InstalledPackageState.Staged, ledger.State);
        Assert.Equal(world.Marketplace.PackageSha256, ledger.PackageSha256);

        var installed = Assert.Single((await administrator.GetAsync(MarketplaceWorld.ApiRoot + "installed")).Json()
            .EnumerateArray()
            .ToList());
        Assert.Equal(MarketplaceWorld.ModuleId, installed.GetProperty("id").GetString());
        Assert.Equal(MarketplaceWorld.ModuleVersion, installed.GetProperty("version").GetString());
        Assert.Equal("installed", installed.GetProperty("lifecycle").GetString());
        Assert.True(installed.GetProperty("restartRequired").GetBoolean());
        Assert.False(installed.GetProperty("dataKept").GetBoolean());

        var status = (await administrator.GetAsync(MarketplaceWorld.ApiRoot + "status")).Json();
        var waiting = Assert.Single(status.GetProperty("restartRequiredModules").EnumerateArray().ToList());
        Assert.Equal(MarketplaceWorld.ModuleId, waiting.GetString());

        var entries = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested);
        var entry = Assert.Single(entries);
        Assert.True(entry.GetProperty("succeeded").GetBoolean());
        var expectedSubject = $"module={MarketplaceWorld.ModuleId};version={MarketplaceWorld.ModuleVersion}"
            + $";digest={world.Marketplace.PackageSha256}";
        Assert.Equal(expectedSubject, entry.GetProperty("subject").GetString());

        var requests = world.Marketplace.Requests;
        var grant = Assert.Single(requests, request => { return IsGrantRequest(request); });
        var download = Assert.Single(requests, request => { return IsDownloadRequest(request); });
        Assert.Equal("Bearer " + MarketplaceWorld.LeaseToken, grant.Authorization);
        Assert.Null(download.Authorization);
        Assert.Empty(Directory.GetFiles(world.PackageDirectory));
    }

    /// <summary>
    /// A wrong password on an install is a 400 carrying <c>ReauthenticationFailed</c> - never a 401 - and
    /// starts nothing and asks the marketplace for nothing; the right password on the same call is accepted.
    /// </summary>
    [Fact]
    public async Task A_wrong_password_on_install_is_a_400_and_starts_nothing_while_the_right_one_is_accepted()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);

        var wrong = await InstallAsync(administrator, acknowledged: true, WrongPassword);

        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Equal("ReauthenticationFailed", wrong.Code());
        Assert.Equal(0, await world.TaskCountAsync(TaskKinds.ModuleInstall));
        Assert.Empty(await world.LedgerAsync());
        Assert.Equal(0, world.Marketplace.RequestCount);
        var refusal = Assert.Single(
            await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested));
        Assert.False(refusal.GetProperty("succeeded").GetBoolean());

        var accepted = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        await world.SettledTaskAsync(accepted.Json().GetProperty("taskId").GetGuid());
        Assert.Equal(1, await world.TaskCountAsync(TaskKinds.ModuleInstall));
    }

    /// <summary>
    /// An install without the acknowledgement is refused with <c>InstallNotAcknowledged</c> before the
    /// marketplace is asked anything, and the refusal is audited; the same call acknowledged is accepted.
    /// </summary>
    [Fact]
    public async Task Installing_without_the_acknowledgement_is_refused_and_audited_while_acknowledging_is_accepted()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);

        var refused = await InstallAsync(administrator, acknowledged: false, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("InstallNotAcknowledged", refused.Code());
        Assert.Equal(0, await world.TaskCountAsync(TaskKinds.ModuleInstall));
        Assert.Empty(await world.LedgerAsync());
        Assert.Equal(0, world.Marketplace.RequestCount);
        var entries = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested);
        var entry = Assert.Single(entries);
        Assert.False(entry.GetProperty("succeeded").GetBoolean());
        Assert.Contains(
            ";reason=InstallNotAcknowledged", entry.GetProperty("subject").GetString(), StringComparison.Ordinal);

        var accepted = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        await world.SettledTaskAsync(accepted.Json().GetProperty("taskId").GetGuid());
        var both = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested);
        Assert.Equal(2, both.Count);
    }

    /// <summary>
    /// A catalogue signed by a key the panel does not pin is shown as unverified - a 200 with no items and a
    /// message, audited - and an install against it is refused with <c>CatalogueUnsigned</c>, opens no task
    /// and never asks for a download grant. The accepted flow above is the same session signed by the pinned key.
    /// </summary>
    [Fact]
    public async Task A_catalogue_signed_by_another_key_is_shown_unverified_and_an_install_against_it_is_refused()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg, signedByPinnedKey: false);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);

        var catalogue = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue");
        var install = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.OK, catalogue.Status);
        var page = catalogue.Json();
        Assert.False(page.GetProperty("verified").GetBoolean());
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(page.GetProperty("unverifiedMessage").GetString()));
        Assert.True(world.Marketplace.RequestCount > 0);
        var verification = await MarketplaceWorld.AuditAsync(
            administrator, MarketplaceClientAuditActions.CatalogueVerificationFailed);
        Assert.NotEmpty(verification);

        Assert.Equal(HttpStatusCode.BadGateway, install.Status);
        Assert.Equal("CatalogueUnsigned", install.Code());
        Assert.Equal(0, await world.TaskCountAsync(TaskKinds.ModuleInstall));
        Assert.Empty(await world.LedgerAsync());
        Assert.DoesNotContain(world.Marketplace.Requests, request => { return IsGrantRequest(request); });
        var refusal = Assert.Single(
            await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested));
        Assert.False(refusal.GetProperty("succeeded").GetBoolean());
    }

    /// <summary>
    /// A download grant that names another host is refused before anything is fetched from it: the task
    /// fails with <c>GrantHostRefused</c>, no request ever leaves for that host, nothing is staged, and the
    /// failure is audited. The accepted flow above is the same grant on the configured host.
    /// </summary>
    [Fact]
    public async Task A_download_grant_naming_another_host_is_refused_and_nothing_is_fetched_from_it()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        world.Marketplace.GrantUrl = "https://evil.example/download/" + FakeMarketplace.DownloadSegment + "/x.ipm";
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);

        var accepted = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        var task = await world.SettledTaskAsync(accepted.Json().GetProperty("taskId").GetGuid());
        Assert.Equal(PanelTaskStatus.Failed, task.Status);
        Assert.Equal("GrantHostRefused", task.ErrorCode);
        Assert.Contains(world.Marketplace.Requests, request => { return IsGrantRequest(request); });
        Assert.DoesNotContain(world.Marketplace.Requests, request => { return request.Host != FakeMarketplace.Host; });
        Assert.DoesNotContain(world.Marketplace.Requests, request => { return IsDownloadRequest(request); });
        Assert.Empty(await world.LedgerAsync());
        Assert.Empty(Directory.GetFiles(world.PackageDirectory));
        var installed = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "installed");
        Assert.Equal(0, installed.Json().GetArrayLength());

        var entries = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested);
        Assert.Contains(entries, entry => { return IsFailureFor(entry, "GrantHostRefused"); });
    }

    /// <summary>
    /// Removing a module keeps its data: the installed list reports it removed with <c>dataKept</c> and no
    /// further action, and the ledger closes its row as removed. A wrong password on the same call is a 400
    /// that leaves the module installed.
    /// </summary>
    [Fact]
    public async Task Removing_a_module_keeps_its_data_and_reports_it_as_removed()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);
        var install = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);
        await world.SettledTaskAsync(install.Json().GetProperty("taskId").GetGuid());
        var removeRoute = MarketplaceWorld.ApiRoot + "modules/" + MarketplaceWorld.ModuleId + "/remove";

        var wrong = await administrator.SendAsync(HttpMethod.Post, removeRoute, new { password = WrongPassword });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Equal("ReauthenticationFailed", wrong.Code());
        var stillThere = await SingleInstalledAsync(administrator);
        Assert.Equal("installed", stillThere.GetProperty("lifecycle").GetString());
        Assert.Equal(0, await world.TaskCountAsync(TaskKinds.ModuleRemove));

        var accepted = await administrator.SendAsync(
            HttpMethod.Post, removeRoute, new { password = EndpointHarness.Password });

        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        var task = await world.SettledTaskAsync(accepted.Json().GetProperty("taskId").GetGuid());
        Assert.Equal(PanelTaskStatus.Completed, task.Status);
        Assert.Equal(TaskKinds.ModuleRemove, task.Kind);
        var removed = await SingleInstalledAsync(administrator);
        Assert.Equal("removed", removed.GetProperty("lifecycle").GetString());
        Assert.True(removed.GetProperty("dataKept").GetBoolean());
        Assert.False(removed.GetProperty("restartRequired").GetBoolean());
        Assert.False(removed.GetProperty("canRemove").GetBoolean());
        var ledger = Assert.Single(await world.LedgerAsync());
        Assert.Equal(InstalledPackageState.Removed, ledger.State);
        var removals = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleRemoveRequested);
        var removal = Assert.Single(removals, entry => { return entry.GetProperty("succeeded").GetBoolean(); });
        var subject = removal.GetProperty("subject").GetString();
        Assert.Contains("module=" + MarketplaceWorld.ModuleId, subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// A customer's install call is refused 403 and writes nothing - no task, no ledger row, no audit entry,
    /// no request to the marketplace - while the identical call as the administrator is accepted.
    /// </summary>
    [Fact]
    public async Task A_customer_install_call_is_refused_and_writes_nothing_while_the_administrator_call_is_accepted()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);
        using var customer = await world.SignInAsync(MarketplaceWorld.CustomerName);

        var refused = await InstallAsync(customer, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal(0, await world.TaskCountAsync(TaskKinds.ModuleInstall));
        Assert.Empty(await world.LedgerAsync());
        Assert.Equal(0, world.Marketplace.RequestCount);
        Assert.Empty(await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested));

        var accepted = await InstallAsync(administrator, acknowledged: true, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        await world.SettledTaskAsync(accepted.Json().GetProperty("taskId").GetGuid());
        Assert.Equal(1, await world.TaskCountAsync(TaskKinds.ModuleInstall));
        Assert.Single(await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.ModuleInstallRequested));
    }

    /// <summary>Tells whether a request is the single-use download-grant request.</summary>
    /// <param name="request">The request the fake marketplace received.</param>
    /// <returns>True for the grant request.</returns>
    private static bool IsGrantRequest(RecordedMarketplaceRequest request)
    {
        return request.Path.EndsWith("/download-grant", StringComparison.Ordinal);
    }

    /// <summary>Tells whether a request is the package download itself.</summary>
    /// <param name="request">The request the fake marketplace received.</param>
    /// <returns>True for the download.</returns>
    private static bool IsDownloadRequest(RecordedMarketplaceRequest request)
    {
        return request.Path.StartsWith("/download/", StringComparison.Ordinal);
    }

    /// <summary>Tells whether an audit entry records a failure for one reason.</summary>
    /// <param name="entry">The entry as the audit API lists it.</param>
    /// <param name="reason">The reason code the subject must end with.</param>
    /// <returns>True when the entry failed and names the reason.</returns>
    private static bool IsFailureFor(JsonElement entry, string reason)
    {
        return !entry.GetProperty("succeeded").GetBoolean()
            && entry.GetProperty("subject").GetString()!.Contains(";reason=" + reason, StringComparison.Ordinal);
    }

    /// <summary>Asks the panel to install the offered version of the offered module.</summary>
    /// <param name="caller">The signed-in caller.</param>
    /// <param name="acknowledged">Whether the caller ticks the acknowledgement.</param>
    /// <param name="password">The password the caller types.</param>
    /// <returns>The panel's answer.</returns>
    private static Task<PanelAnswer> InstallAsync(PanelCaller caller, bool acknowledged, string password)
    {
        return caller.SendAsync(
            HttpMethod.Post,
            MarketplaceWorld.ApiRoot + "modules/" + MarketplaceWorld.ModuleId + "/install",
            new { version = MarketplaceWorld.ModuleVersion, acknowledged, password });
    }

    /// <summary>Reads the installed list, which must hold exactly one module.</summary>
    /// <param name="caller">The signed-in administrator.</param>
    /// <returns>The one module's entry.</returns>
    private static async Task<JsonElement> SingleInstalledAsync(PanelCaller caller)
    {
        var answer = await caller.GetAsync(MarketplaceWorld.ApiRoot + "installed");
        Assert.Equal(HttpStatusCode.OK, answer.Status);

        return Assert.Single(answer.Json().EnumerateArray().ToList());
    }
}
