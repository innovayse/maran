using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Services;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>The doubles a command handler is built from, kept together so each test states only what it changes.</summary>
public sealed class AppsWorld : IDisposable
{
    /// <summary>The instant the clock reports.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Creates the world.</summary>
    /// <param name="saveFailure">A failure every save of the ledger raises, or null for a database that accepts writes.</param>
    public AppsWorld(DbUpdateException? saveFailure = null)
    {
        Db = AppsTestContext.Create(
            DatabaseName,
            saveFailure is null ? null : new FailingSaveInterceptor(saveFailure));
    }

    /// <summary>Creates the world over a ledger whose saves pass through an interceptor.</summary>
    /// <param name="interceptor">The interceptor standing in for what the real database does on a save.</param>
    public AppsWorld(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor interceptor)
    {
        Db = AppsTestContext.Create(DatabaseName, interceptor);
    }

    /// <summary>The name of the in-memory database, so a second context can act as a rival request.</summary>
    public string DatabaseName { get; } = Guid.NewGuid().ToString();

    /// <summary>The ledger.</summary>
    public AppsDbContext Db { get; }

    /// <summary>Opens a second context over the same ledger: the view another request has of it.</summary>
    /// <returns>A new context; the caller disposes it.</returns>
    public AppsDbContext RivalDb()
    {
        return AppsTestContext.Create(DatabaseName);
    }

    /// <summary>The agent.</summary>
    public FakeAgentAppsClient Agent { get; } = new();

    /// <summary>The task recorder.</summary>
    public RecordingTaskRecorder Tasks { get; } = new();

    /// <summary>The audit writer.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The dispatcher.</summary>
    public RecordingAppOperationDispatcher Dispatcher { get; } = new();

    /// <summary>The sites that use runtimes.</summary>
    public FakeSiteRuntimeUsage Sites { get; } = new();

    /// <summary>The clock.</summary>
    public FakeClock Clock { get; } = new(Now);

    /// <summary>The journal over <see cref="Audit"/>.</summary>
    public AppsAuditJournal Journal
    {
        get { return new AppsAuditJournal(Audit, new FakeCurrentUser()); }
    }

    /// <summary>Scripts the agent to offer the given ids as installable.</summary>
    /// <param name="ids">The catalogue ids the host's sources offer.</param>
    public void Offer(params string[] ids)
    {
        Agent.Installable = Maran.SharedKernel.Results.Result<IReadOnlyList<AgentInstallableApp>>.Ok(
            ids.Select(id => { return new AgentInstallableApp(id, AgentAppKind.WebServer, false); }).ToList());
    }

    /// <summary>Whether a task of <paramref name="kind"/> was opened.</summary>
    /// <param name="kind">The task kind.</param>
    /// <returns>True when one was.</returns>
    public bool OpenedTask(string kind)
    {
        return Tasks.Tasks.Any(task => { return task.Kind == kind; });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Db.Dispose();
    }
}
