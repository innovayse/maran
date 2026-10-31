using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Queries.ListTasks;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Tasks.Tests.Queries.ListTasks;

/// <summary>
/// Who the tasks listing answers, and with what: one page of the tasks the caller may see, narrowed by
/// account, status, kind and window. The subject search is an <c>ILIKE</c> only PostgreSQL runs, so
/// it is held by <c>TaskListEndpointTests</c>.
/// </summary>
public sealed class ListTasksQueryHandlerTests
{
    /// <summary>An administrator sees the panels tasks newest first.</summary>
    [Fact]
    public async Task An_administrator_sees_the_panels_tasks_newest_first()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row(
                subject: "older.example", startedAt: TasksTestContext.Now.AddMinutes(-30)));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "newer.example"));
            await seed.SaveChangesAsync();
        }

        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var handler = new ListTasksQueryHandler(
            context, admin, TasksTestContext.KindNames(), TasksTestContext.FailureMessages());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["newer.example", "older.example"], result.Value.Items.Select(task =>
        {
            return task.Subject;
        }));
    }

    /// <summary>A customer listing tasks gets only their own account's tasks, each with an empty log.</summary>
    /// <remarks>
    /// Three rows, one per kind of owner: the customer's own, another tenant's, and one that belongs
    /// to no account. Only the first may come back, and its log must not — the log is operator
    /// material (rules/security.md item 8).
    /// </remarks>
    [Fact]
    public async Task A_customer_listing_tasks_gets_only_their_own_accounts_tasks_each_with_an_empty_log()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        var own = TasksTestContext.Row(subject: "own.example", accountId: accountId);
        own.Report(50, "tool output");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(own);
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "other.example", accountId: Guid.NewGuid()));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "server.example"));
            await seed.SaveChangesAsync();
        }

        var customer = FakeCurrentUser.Customer(accountId);
        await using var context = TasksTestContext.Create(customer, database);
        var handler = new ListTasksQueryHandler(
            context, customer, TasksTestContext.KindNames(), TasksTestContext.FailureMessages());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.Value.Items);
        Assert.Equal(1, result.Value.Total);
        Assert.Equal(own.Id, task.Id);
        Assert.False(task.LogIncluded);
        Assert.Equal(string.Empty, task.Log);
    }

    /// <summary>A customer with no tasks of their own gets an empty list rather than not found.</summary>
    [Fact]
    public async Task A_customer_with_no_tasks_of_their_own_gets_an_empty_list_rather_than_not_found()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row());
            await seed.SaveChangesAsync();
        }

        var customer = FakeCurrentUser.Customer();
        await using var context = TasksTestContext.Create(customer, database);
        var handler = new ListTasksQueryHandler(
            context, customer, TasksTestContext.KindNames(), TasksTestContext.FailureMessages());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>An administrator listing tasks is sent each task's log.</summary>
    /// <remarks>The inverse control for the redaction above: a mapper that dropped every log would pass it.</remarks>
    [Fact]
    public async Task An_administrator_listing_tasks_is_sent_each_tasks_log()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row(accountId: Guid.NewGuid());
        task.Report(50, "tool output");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var handler = new ListTasksQueryHandler(
            context, admin, TasksTestContext.KindNames(), TasksTestContext.FailureMessages());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        var listed = Assert.Single(result.Value.Items);
        Assert.True(listed.LogIncluded);
        Assert.Equal("tool output", listed.Log);
    }

    /// <summary>A page holds no more rows than its size, the total counts every match, and a page past the end is empty.</summary>
    [Fact]
    public async Task A_page_holds_its_size_and_the_total_counts_every_match()
    {
        var database = Guid.NewGuid().ToString();
        var rows = Enumerable.Range(0, 12)
            .Select(minute =>
            {
                return TasksTestContext.Row(
                    subject: $"task-{minute:D2}", startedAt: TasksTestContext.Now.AddMinutes(minute));
            })
            .ToArray();
        await SeedAsync(database, rows);

        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var handler = new ListTasksQueryHandler(
            context, admin, TasksTestContext.KindNames(), TasksTestContext.FailureMessages());
        var second = await handler.HandleAsync(new ListTasksQuery(Page: 2, PageSize: 10), CancellationToken.None);
        var past = await handler.HandleAsync(new ListTasksQuery(Page: 9, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, second.Value.Total);
        Assert.Equal(["task-01", "task-00"], second.Value.Items.Select(task =>
        {
            return task.Subject;
        }));
        Assert.Equal(12, past.Value.Total);
        Assert.Empty(past.Value.Items);
    }

    /// <summary>The subject sort orders by subject in both directions.</summary>
    [Fact]
    public async Task The_subject_sort_orders_by_subject_in_both_directions()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(
            database,
            TasksTestContext.Row(subject: "b.example"),
            TasksTestContext.Row(subject: "a.example"),
            TasksTestContext.Row(subject: "c.example"));

        Assert.Equal(
            ["a.example", "b.example", "c.example"],
            await AdminSubjectsAsync(database, new ListTasksQuery(Sort: SortRequest.Parse("subject"))));
        Assert.Equal(
            ["c.example", "b.example", "a.example"],
            await AdminSubjectsAsync(database, new ListTasksQuery(Sort: SortRequest.Parse("-subject"))));
    }

    /// <summary>The status, kind and account filters each keep only their own rows.</summary>
    [Fact]
    public async Task The_status_kind_and_account_filters_keep_only_their_rows()
    {
        var database = Guid.NewGuid().ToString();
        var account = Guid.NewGuid();
        var failed = TasksTestContext.Row(subject: "failed.example");
        failed.Fail("AgentSystemFailure", TasksTestContext.Now);
        await SeedAsync(
            database,
            failed,
            TasksTestContext.Row(kind: "BackupCreate", subject: "backup.example"),
            TasksTestContext.Row(subject: "owned.example", accountId: account));

        Assert.Equal(
            ["failed.example"],
            await AdminSubjectsAsync(database, new ListTasksQuery(Status: PanelTaskStatus.Failed)));
        Assert.Equal(
            ["backup.example"],
            await AdminSubjectsAsync(database, new ListTasksQuery(Kind: FilterValue.Of("BackupCreate"))));
        Assert.Equal(
            ["owned.example"],
            await AdminSubjectsAsync(database, new ListTasksQuery(AccountId: account)));
    }

    /// <summary>The start window is inclusive at both ends and leaves out what lies outside it.</summary>
    [Fact]
    public async Task The_start_window_is_inclusive_at_both_ends()
    {
        var database = Guid.NewGuid().ToString();
        var start = TasksTestContext.Now;
        await SeedAsync(
            database,
            TasksTestContext.Row(subject: "before", startedAt: start.AddDays(-1)),
            TasksTestContext.Row(subject: "start", startedAt: start),
            TasksTestContext.Row(subject: "end", startedAt: start.AddDays(1)),
            TasksTestContext.Row(subject: "after", startedAt: start.AddDays(2)));

        Assert.Equal(
            ["end", "start"],
            await AdminSubjectsAsync(database, new ListTasksQuery(From: start, To: start.AddDays(1))));
    }

    /// <summary>
    /// A customer naming another tenant's account, or filtering for a kind only that tenant's or an
    /// administrator's tasks carry, gets an empty page with a total of zero.
    /// </summary>
    [Fact]
    public async Task A_customer_filtering_for_rows_they_may_not_see_gets_a_total_of_zero()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await SeedAsync(
            database,
            TasksTestContext.Row(kind: "BackupCreate", subject: "mine.example", accountId: mine),
            TasksTestContext.Row(kind: "BackupRestore", subject: "theirs.example", accountId: theirs),
            TasksTestContext.Row(kind: "PhpVersionInstall", subject: "server"),
            TasksTestContext.Row(
                kind: "AccountSuspension", subject: "suspended", accountId: mine, audience: TaskAudience.AdministratorOnly));

        var customer = FakeCurrentUser.Customer(mine);
        await using var context = TasksTestContext.Create(customer, database);
        var handler = new ListTasksQueryHandler(
            context, customer, TasksTestContext.KindNames(), TasksTestContext.FailureMessages());

        var byAccount = await handler.HandleAsync(new ListTasksQuery(AccountId: theirs), CancellationToken.None);
        var byRestore = await handler.HandleAsync(new ListTasksQuery(Kind: FilterValue.Of("BackupRestore")), CancellationToken.None);
        var bySuspension = await handler.HandleAsync(new ListTasksQuery(Kind: FilterValue.Of("AccountSuspension")), CancellationToken.None);
        var own = await handler.HandleAsync(new ListTasksQuery(AccountId: mine), CancellationToken.None);

        Assert.Equal(0, byAccount.Value.Total);
        Assert.Equal(0, byRestore.Value.Total);
        Assert.Equal(0, bySuspension.Value.Total);
        Assert.Equal(["mine.example"], own.Value.Items.Select(task =>
        {
            return task.Subject;
        }));
    }

    /// <summary>Seeds rows into one database as an administrator.</summary>
    /// <param name="database">The in-memory database to seed.</param>
    /// <param name="rows">The rows to add.</param>
    /// <returns>Resolves once the rows are saved.</returns>
    private static async Task SeedAsync(string database, params PanelTask[] rows)
    {
        await using var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        seed.PanelTasks.AddRange(rows);
        await seed.SaveChangesAsync();
    }

    /// <summary>Reads one page as an administrator and answers the subjects on it, in order.</summary>
    /// <param name="database">The in-memory database to read.</param>
    /// <param name="query">The page to read.</param>
    /// <returns>The subjects of the page's rows.</returns>
    private static async Task<IReadOnlyList<string>> AdminSubjectsAsync(string database, ListTasksQuery query)
    {
        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var result = await new ListTasksQueryHandler(
            context, admin, TasksTestContext.KindNames(), TasksTestContext.FailureMessages())
            .HandleAsync(query, CancellationToken.None);
        return result.Value.Items
            .Select(task =>
            {
                return task.Subject;
            })
            .ToList();
    }
}
