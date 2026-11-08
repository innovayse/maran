using Maran.Modules.Monitoring.Commands.SaveAlertThresholds;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Monitoring.Tests.Commands.SaveAlertThresholds;

/// <summary>Saving the alert thresholds: stored as the one row, and journalled with the figures before and after.</summary>
public sealed class SaveAlertThresholdsCommandHandlerTests
{
    /// <summary>The first save creates the one stored row with the chosen figures.</summary>
    [Fact]
    public async Task The_first_save_creates_the_one_stored_row_with_the_chosen_figures()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var handler = CreateHandler(dbContext, new RecordingAuditWriter());

        var result = await handler.HandleAsync(new SaveAlertThresholdsCommand(85, 70, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(await dbContext.AlertThresholds.ToListAsync());
        Assert.Equal(AlertThresholds.SingletonId, row.Id);
        Assert.Equal(85, row.DiskPercent);
        Assert.Equal(70, row.CpuPercent);
        Assert.Null(row.MemoryPercent);
    }

    /// <summary>A second save replaces the figures rather than adding a row.</summary>
    [Fact]
    public async Task A_second_save_replaces_the_figures_rather_than_adding_a_row()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var handler = CreateHandler(dbContext, new RecordingAuditWriter());

        await handler.HandleAsync(new SaveAlertThresholdsCommand(85, 70, 60), CancellationToken.None);
        await handler.HandleAsync(new SaveAlertThresholdsCommand(95, null, 65), CancellationToken.None);

        var row = Assert.Single(await dbContext.AlertThresholds.ToListAsync());
        Assert.Equal(95, row.DiskPercent);
        Assert.Null(row.CpuPercent);
        Assert.Equal(65, row.MemoryPercent);
    }

    /// <summary>The journal entry names the caller and all three figures before and after.</summary>
    [Fact]
    public async Task The_journal_entry_names_the_caller_and_all_three_figures_before_and_after()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var audit = new RecordingAuditWriter();
        var handler = CreateHandler(dbContext, audit);

        await handler.HandleAsync(
            new SaveAlertThresholdsCommand(99, 80, null, "198.51.100.7", "browser"), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(MonitoringAuditActions.AlertThresholdsSaved, entry.Action);
        Assert.Equal("disk=90;cpu=off;memory=off -> disk=99;cpu=80;memory=off", entry.Subject);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("198.51.100.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Builds the handler over the given collaborators.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="audit">Where journal entries land.</param>
    /// <returns>The handler.</returns>
    private static SaveAlertThresholdsCommandHandler CreateHandler(MonitoringDbContext dbContext, RecordingAuditWriter audit)
    {
        return new SaveAlertThresholdsCommandHandler(
            dbContext,
            new MonitoringAuditJournal(audit, new FakeCurrentUser()),
            new FakeClock());
    }
}
