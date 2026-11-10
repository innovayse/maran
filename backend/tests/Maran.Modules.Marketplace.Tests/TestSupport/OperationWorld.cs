using Maran.Modules.Marketplace.Commands.InstallModule;
using Maran.Modules.Marketplace.Commands.RemoveModule;
using Maran.Modules.Marketplace.Commands.RollbackModule;
using Maran.Modules.Marketplace.Commands.SetMarketplaceEnabled;
using Maran.Modules.Marketplace.Commands.UpdateModule;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Persistence;
using Maran.Modules.Marketplace.Resources;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// The marketplace module's operation half assembled over fakes: a real ledger, gate, switch store and
/// eligibility rules on an in-memory database, with the marketplace, the loader, the password check, the
/// task recorder and the bus replaced. A test starts an operation through a handler, then runs what the
/// dispatcher was handed with <see cref="RunDispatchedAsync"/>, exactly as the bus would.
/// </summary>
public sealed class OperationWorld : IDisposable
{
    /// <summary>The folder downloaded files are written to.</summary>
    private readonly string _directory = Directory.CreateTempSubdirectory("maran-operation-test-").FullName;

    /// <summary>The contexts opened by <see cref="InstallHandlerAfterRestart"/>, disposed with the world.</summary>
    private readonly List<MarketplaceDbContext> _restartedContexts = [];

    /// <summary>The name of the in-memory database, so a second context can see the same rows.</summary>
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    /// <summary>The user the fake current user signs in as.</summary>
    public Guid UserId { get; } = AdminCurrentUser.UserId;

    /// <summary>The clock.</summary>
    public AdvancingClock Clock { get; } = new();

    /// <summary>The module's database context.</summary>
    public MarketplaceDbContext Context { get; }

    /// <summary>The marketplace client.</summary>
    public FakeMarketplaceClient Client { get; }

    /// <summary>The module loader.</summary>
    public FakeModuleLoader Loader { get; } = new();

    /// <summary>The task recorder.</summary>
    public FakeTaskRecorder Tasks { get; } = new();

    /// <summary>The password check.</summary>
    public FakeReauthentication Reauthentication { get; } = new();

    /// <summary>The audit writer, which keeps every entry.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The bus stand-in that keeps the operations it was handed.</summary>
    public RecordingModuleOperationDispatcher Dispatcher { get; } = new();

    /// <summary>The ledger of what was staged.</summary>
    public InstalledPackageLedger Ledger { get; }

    /// <summary>The per-module operation gate.</summary>
    public ModuleOperationGate Gate { get; }

    /// <summary>The stored switch.</summary>
    public MarketplaceSettingsStore Settings { get; }

    /// <summary>The installed-module reader.</summary>
    public InstalledModuleReader Installed { get; }

    /// <summary>The eligibility rules.</summary>
    public InstallEligibility Eligibility { get; }

    /// <summary>The words, echoing codes and keys.</summary>
    public MarketplaceDisplayNames Names { get; }

    /// <summary>The catalogue cache.</summary>
    public CatalogueCache Cache { get; }

    /// <summary>What the last update check found withdrawn.</summary>
    public ObservedRevocations Revocations { get; } = new();

    /// <summary>The validated options reader.</summary>
    public MarketplaceOptionsReader OptionsReader { get; }

    /// <summary>The audit journal.</summary>
    public MarketplaceAuditJournal Journal { get; }

    /// <summary>The front half every operation shares.</summary>
    public ModuleOperationStarter Starter { get; }

    /// <summary>The runner of accepted operations.</summary>
    public ModuleOperationRunner Runner { get; }

    /// <summary>Turns catalogue answers into wire shapes.</summary>
    public MarketplaceCatalogueProjector Projector { get; }

    /// <summary>Reads the marketplace status.</summary>
    public MarketplaceStatusReader Status { get; }

    /// <summary>Creates the world, with the marketplace switched on by default.</summary>
    public OperationWorld()
    {
        Context = MarketplaceDatabase.Create(_databaseName);
        Client = new FakeMarketplaceClient(_directory);
        Ledger = new InstalledPackageLedger(Context, Clock);
        Gate = new ModuleOperationGate(Context, Clock);
        Settings = new MarketplaceSettingsStore(Context, Clock);
        Installed = new InstalledModuleReader(Loader, Ledger, Revocations);
        Eligibility = new InstallEligibility(Settings, Installed, Ledger, Context, new FixedPanelVersion(), Clock);
        Names = new MarketplaceDisplayNames(new EchoErrorTextProvider(), new KeyLocalizer<DisplayNames>());
        OptionsReader = new MarketplaceOptionsReader(new TestOptionsMonitor<MarketplaceOptions>(
            new MarketplaceOptions { BaseUrl = "https://marketplace.test" }));
        Cache = new CatalogueCache(OptionsReader, Clock);
        Journal = new MarketplaceAuditJournal(Audit, new AdminCurrentUser());
        Starter = new ModuleOperationStarter(
            Reauthentication, Gate, Journal, Tasks, new FixedCorrelationIdAccessor(), Dispatcher);
        Runner = new ModuleOperationRunner(
            Client, Loader, Ledger, Gate, Journal, Tasks, Cache, NullLogger<ModuleOperationRunner>.Instance);
        Projector = new MarketplaceCatalogueProjector(Eligibility, Names);
        Status = new MarketplaceStatusReader(Settings, OptionsReader, Installed, Names);

        Settings.EnableAsync(UserId, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Builds the install handler.</summary>
    /// <returns>The handler.</returns>
    public InstallModuleCommandHandler InstallHandler()
    {
        return new InstallModuleCommandHandler(Starter, new ModuleOfferResolver(Client, Eligibility), Eligibility);
    }

    /// <summary>
    /// Builds an install handler the way a restarted panel would have it: a new database context, gate and
    /// starter over the SAME stored rows, with nothing carried over in memory from the first handler.
    /// </summary>
    /// <returns>The handler.</returns>
    public InstallModuleCommandHandler InstallHandlerAfterRestart()
    {
        var context = MarketplaceDatabase.Create(_databaseName);
        _restartedContexts.Add(context);
        var starter = new ModuleOperationStarter(
            Reauthentication,
            new ModuleOperationGate(context, Clock),
            Journal,
            Tasks,
            new FixedCorrelationIdAccessor(),
            Dispatcher);
        return new InstallModuleCommandHandler(starter, new ModuleOfferResolver(Client, Eligibility), Eligibility);
    }

    /// <summary>Builds the remove handler the way a restarted panel would have it; see <see cref="InstallHandlerAfterRestart"/>.</summary>
    /// <returns>The handler.</returns>
    public RemoveModuleCommandHandler RemoveHandlerAfterRestart()
    {
        var context = MarketplaceDatabase.Create(_databaseName);
        _restartedContexts.Add(context);
        var starter = new ModuleOperationStarter(
            Reauthentication,
            new ModuleOperationGate(context, Clock),
            Journal,
            Tasks,
            new FixedCorrelationIdAccessor(),
            Dispatcher);
        return new RemoveModuleCommandHandler(starter, Installed);
    }

    /// <summary>Builds the update handler.</summary>
    /// <returns>The handler.</returns>
    public UpdateModuleCommandHandler UpdateHandler()
    {
        return new UpdateModuleCommandHandler(Starter, new ModuleOfferResolver(Client, Eligibility), Eligibility);
    }

    /// <summary>Builds the remove handler.</summary>
    /// <returns>The handler.</returns>
    public RemoveModuleCommandHandler RemoveHandler()
    {
        return new RemoveModuleCommandHandler(Starter, Installed);
    }

    /// <summary>Builds the rollback handler.</summary>
    /// <returns>The handler.</returns>
    public RollbackModuleCommandHandler RollbackHandler()
    {
        return new RollbackModuleCommandHandler(Starter, Installed);
    }

    /// <summary>Builds the switch handler.</summary>
    /// <returns>The handler.</returns>
    public SetMarketplaceEnabledCommandHandler SwitchHandler()
    {
        return new SetMarketplaceEnabledCommandHandler(Reauthentication, Settings, Journal, Cache, Status);
    }

    /// <summary>An install command carrying the right password and the acknowledgement, stamped as the controller stamps it.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The version.</param>
    /// <returns>The command.</returns>
    public InstallModuleCommand Install(string moduleId, string version)
    {
        return new InstallModuleCommand(
            version, Acknowledged: true, FakeReauthentication.ValidPassword, moduleId, UserId, "203.0.113.5", "test-agent");
    }

    /// <summary>An update command carrying the right password and the acknowledgement, stamped as the controller stamps it.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The version.</param>
    /// <returns>The command.</returns>
    public UpdateModuleCommand Update(string moduleId, string version)
    {
        return new UpdateModuleCommand(
            version, Acknowledged: true, FakeReauthentication.ValidPassword, moduleId, UserId, "203.0.113.5", "test-agent");
    }

    /// <summary>A remove command carrying the right password, stamped as the controller stamps it.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <returns>The command.</returns>
    public RemoveModuleCommand Remove(string moduleId)
    {
        return new RemoveModuleCommand(FakeReauthentication.ValidPassword, moduleId, UserId, "203.0.113.5", "test-agent");
    }

    /// <summary>A rollback command carrying the right password, stamped as the controller stamps it.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <returns>The command.</returns>
    public RollbackModuleCommand Rollback(string moduleId)
    {
        return new RollbackModuleCommand(FakeReauthentication.ValidPassword, moduleId, UserId, "203.0.113.5", "test-agent");
    }

    /// <summary>Registers a module in the fake marketplace with its versions, entitled by default.</summary>
    /// <param name="module">The module.</param>
    /// <param name="versions">Every version on offer.</param>
    public void Offer(CatalogueModule module, params CatalogueVersion[] versions)
    {
        Client.Details[module.Id] = CatalogueSamples.Details(module, versions);
    }

    /// <summary>Runs every operation the dispatcher was handed, the way the bus would, and clears the list.</summary>
    /// <returns>A task that completes when they have all run.</returns>
    public async Task RunDispatchedAsync()
    {
        var pending = Dispatcher.Dispatched.ToList();
        Dispatcher.Dispatched.Clear();

        foreach (var request in pending)
        {
            await Runner.RunAsync(request, CancellationToken.None);
        }
    }

    /// <summary>The audit entries written for one action, in order.</summary>
    /// <param name="action">The action name.</param>
    /// <returns>The matching entries.</returns>
    public List<AuditEntry> AuditFor(string action)
    {
        return Audit.Entries.Where(entry => { return entry.Action == action; }).ToList();
    }

    /// <summary>Deletes the downloaded files folder.</summary>
    public void Dispose()
    {
        Context.Dispose();
        foreach (var context in _restartedContexts)
        {
            context.Dispose();
        }

        Directory.Delete(_directory, recursive: true);
    }
}
