using Maran.Modules.Cron.Commands.RunCronEntry;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Options;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>Everything one run test needs, wired the way the Host wires it.</summary>
public sealed class RunCronEntryWorld
{
    /// <summary>The agent's identifier for the entry every test runs.</summary>
    public const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    /// <summary>The account a world owns unless a test names another.</summary>
    public static readonly Guid DefaultAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The account this world's caller owns.</summary>
    private readonly Guid _ownerId;

    /// <summary>The agent double every call is recorded on.</summary>
    public RecordingAgentCronClient Agent { get; } = new();

    /// <summary>The journal every entry lands in.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The panel's clock, fixed.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The module's table.</summary>
    public CronDbContext Database { get; }

    /// <summary>The bound on runs in flight; share one between worlds to count them together.</summary>
    public CronRunSlots Slots { get; }

    /// <summary>The handler under test.</summary>
    public RunCronEntryCommandHandler Handler { get; }

    /// <summary>Wires one world whose account is in <paramref name="state"/>.</summary>
    /// <param name="state">The owning account's state.</param>
    /// <param name="slots">The counter of runs in flight; a roomy one of its own by default.</param>
    /// <param name="ownerId">The account the caller owns; <see cref="DefaultAccountId"/> by default.</param>
    public RunCronEntryWorld(
        AccountState state = AccountState.Active,
        CronRunSlots? slots = null,
        Guid? ownerId = null)
    {
        _ownerId = ownerId ?? DefaultAccountId;
        Slots = slots ?? Bounded(perAccount: 100, global: 100);
        var currentUser = FakeCurrentUser.Customer(_ownerId);
        Database = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), currentUser);
        Handler = new RunCronEntryCommandHandler(
            new StubAccountDirectory(new AccountSnapshot(_ownerId, "alice", 5, 5, 5, 5, 5, 1_024, state)),
            Agent,
            Database,
            Clock,
            Slots,
            new CronAuditJournal(Audit, currentUser),
            new CapturingLogger<RunCronEntryCommandHandler>(),
            CronTestNames.Runs());
    }

    /// <summary>A counter with the given bounds.</summary>
    /// <param name="perAccount">The most runs in flight for one account.</param>
    /// <param name="global">The most runs in flight in total.</param>
    /// <returns>The counter.</returns>
    public static CronRunSlots Bounded(int perAccount, int global)
    {
        return new CronRunSlots(
            new StaticOptionsMonitor(
                new CronRunOptions { MaxConcurrentPerAccount = perAccount, MaxConcurrentGlobal = global }));
    }

    /// <summary>Runs the handler with the usual audit context.</summary>
    /// <param name="accountId">The account to act under; the owner by default.</param>
    /// <param name="cancellationToken">The request's token; never cancelled by default.</param>
    /// <returns>What the handler answered.</returns>
    public async Task<Result<CronRunDto>> HandleAsync(Guid? accountId = null, CancellationToken cancellationToken = default)
    {
        return await Handler.HandleAsync(
            new RunCronEntryCommand(accountId ?? _ownerId, EntryId, "203.0.113.7", "tests"),
            cancellationToken);
    }

    /// <summary>An <see cref="IOptionsMonitor{TOptions}"/> that always answers one value.</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<CronRunOptions>
    {
        /// <summary>Creates the monitor.</summary>
        /// <param name="value">The value every read answers.</param>
        public StaticOptionsMonitor(CronRunOptions value)
        {
            CurrentValue = value;
        }

        /// <inheritdoc />
        public CronRunOptions CurrentValue { get; }

        /// <inheritdoc />
        public CronRunOptions Get(string? name)
        {
            return CurrentValue;
        }

        /// <inheritdoc />
        public IDisposable? OnChange(Action<CronRunOptions, string?> listener)
        {
            return null;
        }
    }
}
