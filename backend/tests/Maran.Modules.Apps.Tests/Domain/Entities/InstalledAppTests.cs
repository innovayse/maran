using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;

namespace Maran.Modules.Apps.Tests.Domain.Entities;

/// <summary>The ledger row's transitions: each is a named method, and each states what it does.</summary>
public sealed class InstalledAppTests
{
    /// <summary>The first instant used.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Creates an installing row for nginx.</summary>
    /// <param name="taskId">The task it carries.</param>
    /// <returns>The row.</returns>
    private static InstalledApp Installing(Guid taskId)
    {
        Assert.True(AppId.TryParse("nginx", out var appId));
        return InstalledApp.BeginInstall(Guid.NewGuid(), appId, taskId, Start);
    }

    /// <summary>An install starts as installing with its task and no install time.</summary>
    [Fact]
    public void An_install_begins_installing_with_its_task()
    {
        var task = Guid.NewGuid();
        var row = Installing(task);

        Assert.Equal(AppState.Installing, row.State);
        Assert.Equal(task, row.TaskId);
        Assert.Null(row.InstalledAt);
        Assert.Equal(AppKind.WebServer, row.Kind);
        Assert.False(row.IsInstalled);
    }

    /// <summary>Completing the install records the version and the first time it was seen installed.</summary>
    [Fact]
    public void Marking_installed_records_the_version_and_the_first_time()
    {
        var row = Installing(Guid.NewGuid());
        row.MarkInstalled("1.24.0", Start.AddMinutes(2));
        row.MarkInstalled("1.26.0", Start.AddDays(30));

        Assert.True(row.IsInstalled);
        Assert.Equal("1.26.0", row.Version);
        Assert.Equal(Start.AddMinutes(2), row.InstalledAt);
        Assert.Equal(Start.AddDays(30), row.UpdatedAt);
    }

    /// <summary>A failed install can be asked for again with a new task.</summary>
    [Fact]
    public void A_failed_install_is_retried_with_a_new_task()
    {
        var row = Installing(Guid.NewGuid());
        row.MarkFailed(Start.AddMinutes(1));
        var second = Guid.NewGuid();

        row.RetryInstall(second, Start.AddMinutes(5));

        Assert.Equal(AppState.Installing, row.State);
        Assert.Equal(second, row.TaskId);
    }

    /// <summary>A removal starts as removing with its task.</summary>
    [Fact]
    public void A_removal_begins_removing_with_its_task()
    {
        var row = Installing(Guid.NewGuid());
        row.MarkInstalled("1.24.0", Start);
        var task = Guid.NewGuid();

        row.BeginRemoval(task, Start.AddHours(1));

        Assert.Equal(AppState.Removing, row.State);
        Assert.Equal(task, row.TaskId);
        Assert.False(row.IsInstalled);
    }

    /// <summary>An app found on the host is installed, has no task and is dated when it was found.</summary>
    [Fact]
    public void A_discovered_app_is_installed_without_a_task()
    {
        Assert.True(AppId.TryParse("apache", out var appId));

        var row = InstalledApp.Discover(Guid.NewGuid(), appId, "2.4.58", Start);

        Assert.True(row.IsInstalled);
        Assert.Null(row.TaskId);
        Assert.Equal(Start, row.InstalledAt);
    }

    /// <summary>Issue #524: a row that settles carries no task, because the field names the operation in flight.</summary>
    /// <param name="installed">Whether the row settles as installed rather than failed.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_settled_row_carries_no_task(bool installed)
    {
        var row = Installing(Guid.NewGuid());

        if (installed)
        {
            row.MarkInstalled("1.24.0", Start);
        }
        else
        {
            row.MarkFailed(Start);
        }

        Assert.Null(row.TaskId);
    }

    /// <summary>The control for the rule above: a row whose operation begins carries that operation's task.</summary>
    [Fact]
    public void A_row_in_flight_carries_the_task_of_its_operation()
    {
        var row = Installing(Guid.NewGuid());
        row.MarkFailed(Start);
        var retry = Guid.NewGuid();

        row.RetryInstall(retry, Start.AddMinutes(1));
        Assert.Equal(retry, row.TaskId);

        row.MarkInstalled("1", Start.AddMinutes(2));
        var removal = Guid.NewGuid();
        row.BeginRemoval(removal, Start.AddMinutes(3));
        Assert.Equal(removal, row.TaskId);
    }

    /// <summary>Issue #526: every transition moves the concurrency token.</summary>
    [Fact]
    public void Every_transition_moves_the_revision()
    {
        var row = Installing(Guid.NewGuid());
        var created = row.Revision;

        row.MarkFailed(Start);
        row.RetryInstall(Guid.NewGuid(), Start);
        row.MarkInstalled("1", Start);
        row.BeginRemoval(Guid.NewGuid(), Start);

        Assert.Equal(created + 4, row.Revision);
    }
}
