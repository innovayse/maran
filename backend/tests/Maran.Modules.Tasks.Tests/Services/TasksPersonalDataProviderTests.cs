using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// The tasks personal-data export: kind, status and times of the named account's tasks, nobody else's,
/// and never the log text.
/// </summary>
public sealed class TasksPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>A line a task logged: stands in for a secret the running work echoed.</summary>
    private const string PlantedLogLine = "PLANTED-LOG-7b2e";

    /// <summary>The fields a task record may carry, and no others.</summary>
    private static readonly string[] AllowedFields = ["id", "kind", "status", "startedAt", "finishedAt"];

    /// <summary>The export holds exactly the allow-listed fields of the named account's tasks.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_tasks()
    {
        var mine = Guid.NewGuid();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        context.PanelTasks.Add(TaskOf(mine));
        context.PanelTasks.Add(TaskOf(Guid.NewGuid()));
        await context.SaveChangesAsync();

        var sections = await new TasksPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var section = Assert.Single(sections);
        var record = Assert.Single(section.Records);
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), record.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("backup.run", record.Fields["kind"]);

        var names = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Keys; })
            .ToList();
        Assert.NotEmpty(names);
        Assert.All(names, name =>
        {
            Assert.DoesNotContain(
                ForbiddenFragments,
                fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); });
        });
    }

    /// <summary>A task's log is stored on the row and is not in the export.</summary>
    /// <remarks>
    /// The positive control is the first assertion: the stored row does hold the planted line, so the
    /// absence asserted after it is a property of the provider and not of an empty row.
    /// </remarks>
    [Fact]
    public async Task A_tasks_log_is_not_exported()
    {
        var mine = Guid.NewGuid();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        context.PanelTasks.Add(TaskOf(mine));
        await context.SaveChangesAsync();
        Assert.Contains(PlantedLogLine, context.PanelTasks.Single().Log, StringComparison.Ordinal);

        var sections = await new TasksPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var values = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Values; })
            .ToList();
        Assert.NotEmpty(values);
        Assert.DoesNotContain(
            values,
            value => { return value is not null && value.Contains("PLANTED", StringComparison.Ordinal); });
    }

    /// <summary>A user subject gets an empty list, because this module holds no row about a login.</summary>
    [Fact]
    public async Task A_user_subject_gets_an_empty_list()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new TasksPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }

    /// <summary>Builds a task of one account whose log holds a planted line.</summary>
    /// <param name="accountId">The account the task concerns.</param>
    /// <returns>The task.</returns>
    private static PanelTask TaskOf(Guid accountId)
    {
        var task = new PanelTask(
            Guid.NewGuid(),
            "backup.run",
            "free text naming a thing",
            accountId,
            TaskAudience.AccountOwner,
            null,
            DateTimeOffset.UnixEpoch);
        task.Report(10, PlantedLogLine);

        return task;
    }
}
