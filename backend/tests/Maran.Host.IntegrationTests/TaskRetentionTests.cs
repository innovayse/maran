using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Jobs;
using Maran.Modules.Tasks.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What one nightly retention pass over <c>tasks.PanelTasks</c> removes, and — the assertion that
/// matters most — what it must never remove.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an integration test and not a unit test.</b> <c>TaskRetentionHandler</c> deletes
/// with <c>ExecuteDeleteAsync</c>, which is translated by the database provider and is not
/// implemented by the in-memory provider the module's unit tests use. A unit test could therefore
/// only assert against a handler that cannot run, which is the shape of test that agrees with any
/// implementation. Driven here against the real PostgreSQL the rest of this assembly boots, the
/// delete is the statement production issues.
/// </para>
/// <para>
/// <b>No HTTP, on purpose.</b> Retention is a scheduled message handler with no endpoint, so the
/// host is booted only to resolve the handler and its context from the real container — the same
/// registration <c>TasksModule</c> makes and <c>TaskRetentionScheduler</c> drives once a day.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TaskRetentionTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>
    /// How many rows the multi-batch test seeds. Larger than the handler's own batch size (500) so
    /// the pass has to loop; if that constant is ever raised past this number the test stops
    /// covering the loop and this comment is where to start.
    /// </summary>
    private const int MoreThanOneBatch = 600;

    /// <summary>Comfortably outside the thirty-day window, whatever hour the suite runs at.</summary>
    private static readonly TimeSpan WellOutsideTheWindow = TimeSpan.FromDays(90);

    /// <summary>Comfortably inside the thirty-day window.</summary>
    private static readonly TimeSpan WellInsideTheWindow = TimeSpan.FromDays(1);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TaskRetentionTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// A pass removes finished tasks older than the window, keeps recent ones, and leaves a task
    /// that is still running exactly where it is however old it is.
    /// </summary>
    [Fact]
    public async Task A_pass_removes_only_finished_tasks_older_than_the_window()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var now = Now(factory);

        // The still-running row is the whole point of this test. It started long before the window
        // and has no FinishedAt, because nothing has closed it — a task abandoned by a crashed
        // process stays running until StartupTaskReconciler explains it on the next boot, and a row
        // nobody has explained yet is a row retention must not make disappear.
        var running = New("CertificateIssue", "running.example", now - WellOutsideTheWindow);

        var oldCompleted = New("CertificateIssue", "completed.example", now - WellOutsideTheWindow);
        oldCompleted.Complete(now - WellOutsideTheWindow);

        // A failure is purged on exactly the same terms as a success: one predicate, one window,
        // and no branch on status anywhere in the handler.
        var oldFailed = New("CertificateIssue", "failed.example", now - WellOutsideTheWindow);
        oldFailed.Fail("AcmeAuthorityUnreachable", now - WellOutsideTheWindow);

        var recentCompleted = New("AccountDelete", "recent.example", now - WellInsideTheWindow);
        recentCompleted.Complete(now - WellInsideTheWindow);

        var recentFailed = New("AccountDelete", "recent-failed.example", now - WellInsideTheWindow);
        recentFailed.Fail("AgentUnavailable", now - WellInsideTheWindow);

        await SeedAsync(factory, [running, oldCompleted, oldFailed, recentCompleted, recentFailed]);

        Assert.Equal(2, await RunPassAsync(factory));

        var survivors = await SurvivingSubjectsAsync(factory);
        Assert.Equal(
            ["recent-failed.example", "recent.example", "running.example"],
            survivors.Order(StringComparer.Ordinal));

        // Not merely present — untouched. A pass that closed the running row instead of skipping it
        // would leave it in the table and still be wrong.
        Assert.Equal(PanelTaskStatus.Running, await StatusOfAsync(factory, running.Id));
    }

    /// <summary>A pass finishes the whole backlog although it deletes in bounded batches.</summary>
    [Fact]
    public async Task A_pass_purges_more_rows_than_one_batch_holds()
    {
        // The answer to "what happens on the first pass after a year with no retention": the delete
        // is split into short statements so it cannot hold its locks over the whole table, and the
        // loop keeps going until nothing qualifies — so bounded must not mean partial.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var now = Now(factory);

        var backlog = new List<PanelTask>(MoreThanOneBatch);
        for (var index = 0; index < MoreThanOneBatch; index++)
        {
            var task = New("CertificateRenew", $"backlog-{index}.example", now - WellOutsideTheWindow);
            task.Complete(now - WellOutsideTheWindow);
            backlog.Add(task);
        }

        await SeedAsync(factory, backlog);

        Assert.Equal(MoreThanOneBatch, await RunPassAsync(factory));
        Assert.Empty(await SurvivingSubjectsAsync(factory));
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    /// <returns>The factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Applies the Tasks schema this test's fresh database does not have yet.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once the table and its FinishedAt index exist.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
    }

    /// <summary>The instant the panel's own clock reports, which the window is measured against.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The clock's current instant.</returns>
    private static DateTimeOffset Now(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
    }

    /// <summary>Builds one task row at revision zero.</summary>
    /// <param name="kind">What kind of operation it records.</param>
    /// <param name="subject">What it acts on; unique per row, so a survivor can be named.</param>
    /// <param name="startedAt">When it started.</param>
    /// <returns>The row.</returns>
    private static PanelTask New(string kind, string subject, DateTimeOffset startedAt)
    {
        return new PanelTask(Guid.NewGuid(), kind, subject, correlationId: null, startedAt);
    }

    /// <summary>Writes the given rows to the module's table.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="tasks">The rows to write.</param>
    /// <returns>Resolves once they are committed.</returns>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory, IReadOnlyList<PanelTask> tasks)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        dbContext.PanelTasks.AddRange(tasks);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Runs one retention pass through the container-resolved handler.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>How many rows the pass deleted.</returns>
    private static async Task<int> RunPassAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<TaskRetentionHandler>();
        return await handler.HandleAsync(new TaskRetentionRequested(), CancellationToken.None);
    }

    /// <summary>Reads back the subjects of every row the pass left behind.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The surviving subjects.</returns>
    /// <remarks>
    /// Read with <c>IgnoreQueryFilters</c> for the same reason the handler writes with it: the
    /// module's filter admits only an administrator, and this reader is nobody at all — a filtered
    /// read would report an empty table whatever the pass did, which is the one answer that would
    /// make both tests here pass on a handler that deleted everything.
    /// </remarks>
    private static async Task<List<string>> SurvivingSubjectsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        return await dbContext.PanelTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(task => task.Subject)
            .ToListAsync();
    }

    /// <summary>Reads back one row's status.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="taskId">The row to read.</param>
    /// <returns>Its stored status.</returns>
    private static async Task<PanelTaskStatus> StatusOfAsync(WebApplicationFactory<Program> factory, Guid taskId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        return await dbContext.PanelTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => task.Status)
            .SingleAsync();
    }
}
