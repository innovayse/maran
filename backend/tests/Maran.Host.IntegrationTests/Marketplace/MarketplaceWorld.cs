using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.IntegrationTests.Modules;
using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Persistence;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// A host booted for the marketplace tests: every shipped module, a real PostgreSQL, and the four things
/// the marketplace reaches that a test must control - the HTTP handler under the named marketplace client
/// (a counting fake marketplace), the pinned metadata key (a file the test writes), the module loader
/// (the contract tests' fake) and the licence-lease authorizer (a known token). Each world owns everything
/// it creates, so no two tests share state.
/// </summary>
/// <remarks>
/// <para>
/// UNOBSERVED HERE: a real package's signature and assemblies (the loader is a fake), the network (the
/// marketplace answers in memory), and a restart (the panel's restart only records that it was asked).
/// What is observed is the panel's own HTTP surface, its database, its audit journal, its tasks, and every
/// request it would have put on the wire.
/// </para>
/// <para>
/// Every log line the panel writes through a logger it resolves from the container is captured, because
/// the container's logger factory is replaced for the world. A line written by code that builds its own
/// logger outside the container would not be seen; the leak test's positive control writes through the
/// container so that a capture which saw nothing fails loudly instead of passing.
/// </para>
/// </remarks>
public sealed class MarketplaceWorld : IAsyncDisposable
{
    /// <summary>The id of the one module the fake marketplace offers.</summary>
    public const string ModuleId = "hello";

    /// <summary>The one version of it the fake marketplace offers.</summary>
    public const string ModuleVersion = "1.0.0";

    /// <summary>The licence-lease token the authorizer attaches: a planted secret of the leak tests.</summary>
    public const string LeaseToken = "lease-secret-7f3a91c2";

    /// <summary>The login name of the seeded customer.</summary>
    public const string CustomerName = "olive";

    /// <summary>The route prefix of the marketplace controller, with its trailing slash.</summary>
    public const string ApiRoot = "/api/v1/marketplace/";

    /// <summary>The pause between two looks at a condition that is expected to become true.</summary>
    private const int PollIntervalMilliseconds = 25;

    /// <summary>How many looks a wait takes before it gives up: twenty seconds in all.</summary>
    private const int PollLimit = 800;

    /// <summary>The container's logger factory, which writes to <see cref="Logs"/>.</summary>
    private readonly ILoggerFactory _loggers;

    /// <summary>The key whose public half the panel pins.</summary>
    private readonly MarketplaceKeyPair _pinned;

    /// <summary>The key the fake marketplace signs with; the pinned one unless the test planted a stranger.</summary>
    private readonly MarketplaceKeyPair _signing;

    /// <summary>The directory holding everything this world wrote to disk.</summary>
    private readonly string _root;

    /// <summary>The booted host.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The fake marketplace behind the panel's named client; it counts every request.</summary>
    public FakeMarketplace Marketplace { get; }

    /// <summary>The authorizer that attaches <see cref="LeaseToken"/> to requests that ask for a lease.</summary>
    public RecordingLeaseAuthorizer Lease { get; }

    /// <summary>Everything the panel logged through the container.</summary>
    public CapturingLoggerProvider Logs { get; }

    /// <summary>The directory packages are downloaded into.</summary>
    public string PackageDirectory { get; }

    /// <summary>Creates the world.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="marketplace">The fake marketplace.</param>
    /// <param name="lease">The lease authorizer.</param>
    /// <param name="logs">The log capture.</param>
    /// <param name="loggers">The container's logger factory.</param>
    /// <param name="pinned">The pinned key pair.</param>
    /// <param name="signing">The key pair the marketplace signs with.</param>
    /// <param name="root">The directory holding everything written to disk.</param>
    /// <param name="packageDirectory">The directory packages are downloaded into.</param>
    private MarketplaceWorld(
        WebApplicationFactory<Program> factory,
        FakeMarketplace marketplace,
        RecordingLeaseAuthorizer lease,
        CapturingLoggerProvider logs,
        ILoggerFactory loggers,
        MarketplaceKeyPair pinned,
        MarketplaceKeyPair signing,
        string root,
        string packageDirectory)
    {
        Factory = factory;
        Marketplace = marketplace;
        Lease = lease;
        Logs = logs;
        _loggers = loggers;
        _pinned = pinned;
        _signing = signing;
        _root = root;
        PackageDirectory = packageDirectory;
    }

    /// <summary>
    /// Boots the host with the marketplace switched off (its default), an administrator named <c>admin</c>
    /// and a customer named <see cref="CustomerName"/>.
    /// </summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="signedByPinnedKey">
    /// True when the fake marketplace signs with the key the panel pins; false when it signs with a stranger's
    /// key, which is what a tampered catalogue looks like.
    /// </param>
    /// <returns>The world, which the caller disposes.</returns>
    public static async Task<MarketplaceWorld> BootAsync(TestDatabase database, bool signedByPinnedKey = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "maran-marketplace-" + Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(root, "packages");
        Directory.CreateDirectory(packageDirectory);

        var pinned = new MarketplaceKeyPair();
        var signing = signedByPinnedKey ? pinned : new MarketplaceKeyPair();
        var keyFile = Path.Combine(root, "marketplace-metadata.pub");
        await File.WriteAllTextAsync(keyFile, pinned.PublicKeyPem);

        var package = FakePackage.Sealed(ModuleId, ModuleVersion).ToBytes();
        var marketplace = new FakeMarketplace(signing, new SystemClock(), ModuleId, ModuleVersion, package);
        var lease = new RecordingLeaseAuthorizer(LeaseToken);
        var licence = new FakeLicenceLease();
        licence.Entitle(ModuleId);
        var restart = new RecordingPanelRestart();
        var logs = new CapturingLoggerProvider();
        var loggers = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });

        var factory = EndpointHarness.CreateFactory(
            database,
            services =>
            {
                services.AddSingleton<ILoggerFactory>(loggers);
                services.AddSingleton<ILicenceLease>(licence);
                services.AddSingleton<IPanelRestart>(restart);
                services.AddSingleton<ILeaseRequestAuthorizer>(lease);
                services.AddSingleton<IModuleLoader>(provider =>
                {
                    return new RemovalReportingModuleLoader(
                        new FakeModuleLoader(provider.GetRequiredService<IServiceScopeFactory>(), licence, restart));
                });
                services.AddHttpClient(MarketplaceOptions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => { return new FakeMarketplaceHandler(marketplace); });
            },
            new Dictionary<string, string>
            {
                ["Marketplace:BaseUrl"] = FakeMarketplace.BaseUrl,
                ["Marketplace:MetadataKeyPath"] = keyFile,
                ["Marketplace:PackageDirectory"] = packageDirectory,
            });

        using (var scope = factory.Services.CreateScope())
        {
            // The version the offered module declares it runs on is the running panel's own, whatever
            // build this is: a pre-release panel is outside a range that names no pre-release.
            marketplace.MinPanelVersion = scope.ServiceProvider.GetRequiredService<IPanelVersion>().Current;
            await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var seeded = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, CustomerName, seeded.AccountId);

        return new MarketplaceWorld(
            factory, marketplace, lease, logs, loggers, pinned, signing, root, packageDirectory);
    }

    /// <summary>Signs a seeded login in.</summary>
    /// <param name="username">The login name: <c>admin</c> or <see cref="CustomerName"/>.</param>
    /// <returns>A caller the test disposes.</returns>
    public async Task<PanelCaller> SignInAsync(string username)
    {
        return new PanelCaller(await EndpointHarness.SignInAsync(Factory, username));
    }

    /// <summary>Switches the marketplace on as the administrator, with the right password.</summary>
    /// <param name="administrator">The signed-in administrator.</param>
    /// <returns>Resolves once the panel has answered 200.</returns>
    public static async Task EnableAsync(PanelCaller administrator)
    {
        var answer = await administrator.SendAsync(
            HttpMethod.Put, ApiRoot + "settings", new { enabled = true, password = EndpointHarness.Password });

        Assert.Equal(HttpStatusCode.OK, answer.Status);
    }

    /// <summary>Reads the audit journal's entries of one action through the administrator's read API.</summary>
    /// <param name="administrator">The signed-in administrator.</param>
    /// <param name="action">The action name to filter on.</param>
    /// <returns>The entries as the API lists them, newest first.</returns>
    public static async Task<IReadOnlyList<JsonElement>> AuditAsync(PanelCaller administrator, string action)
    {
        var answer = await administrator.GetAsync("/api/v1/audit?pageSize=100&action=" + Uri.EscapeDataString(action));
        Assert.Equal(HttpStatusCode.OK, answer.Status);

        return answer.Json().GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>
    /// Waits for an accepted operation to finish: its task has left the running state and the module's
    /// operation lock is released, so the next call on the same module is not a conflict.
    /// </summary>
    /// <param name="taskId">The id the panel answered 202 with.</param>
    /// <returns>The finished task row.</returns>
    public async Task<PanelTask> SettledTaskAsync(Guid taskId)
    {
        for (var poll = 0; poll < PollLimit; poll++)
        {
            using (var scope = Factory.Services.CreateScope())
            {
                var task = await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
                    .PanelTasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == taskId);
                var locks = await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>()
                    .ModuleOperationLocks.AsNoTracking().CountAsync();

                if (task.Status != PanelTaskStatus.Running && locks == 0)
                {
                    return task;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        }

        throw new TimeoutException($"Task {taskId} did not settle within {PollLimit * PollIntervalMilliseconds} ms.");
    }

    /// <summary>Reads every row of the installed-package ledger.</summary>
    /// <returns>The rows, oldest first.</returns>
    public async Task<IReadOnlyList<InstalledPackage>> LedgerAsync()
    {
        using var scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>()
            .InstalledPackages.AsNoTracking().OrderBy(row => row.InstalledAt).ToListAsync();
    }

    /// <summary>Counts the task rows of one kind, whoever owns them.</summary>
    /// <param name="kind">The task kind.</param>
    /// <returns>The number of rows.</returns>
    public async Task<int> TaskCountAsync(string kind)
    {
        using var scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .PanelTasks.IgnoreQueryFilters().CountAsync(row => row.Kind == kind);
    }

    /// <summary>Tells whether the marketplace switch has ever been written.</summary>
    /// <returns>True when the settings row exists.</returns>
    public async Task<bool> SettingsRowExistsAsync()
    {
        using var scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>()
            .MarketplaceSettings.AsNoTracking().AnyAsync();
    }

    /// <summary>Writes one log line through the logger factory the container hands out.</summary>
    /// <param name="message">The line.</param>
    public void LogThroughTheContainer(string message)
    {
        Factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MarketplaceTests.Planted")
            .Log(LogLevel.Information, new EventId(1), message, null, static (state, _) => { return state; });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        _loggers.Dispose();
        _pinned.Dispose();
        if (!ReferenceEquals(_signing, _pinned))
        {
            _signing.Dispose();
        }

        Directory.Delete(_root, recursive: true);
    }
}
