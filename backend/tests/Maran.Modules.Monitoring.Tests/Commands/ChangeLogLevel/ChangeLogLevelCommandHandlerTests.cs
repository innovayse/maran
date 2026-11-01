using Maran.Modules.Monitoring.Commands.ChangeLogLevel;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Monitoring.Tests.Commands.ChangeLogLevel;

/// <summary>Changing the panel's log level: stored, applied to the running logger, and journalled.</summary>
public sealed class ChangeLogLevelCommandHandlerTests
{
    /// <summary>A change is applied to the running logger at once.</summary>
    [Fact]
    public async Task A_change_is_applied_to_the_running_logger_at_once()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var levelSwitch = new FakePanelLogLevelSwitch();
        var handler = CreateHandler(dbContext, levelSwitch, new RecordingAuditWriter());

        var result = await handler.HandleAsync(
            new ChangeLogLevelCommand(PanelLogLevel.Debug), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PanelLogLevel.Debug, levelSwitch.Current);
    }

    /// <summary>The first change creates the one stored row with the chosen level.</summary>
    [Fact]
    public async Task The_first_change_creates_the_one_stored_row_with_the_chosen_level()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var handler = CreateHandler(dbContext, new FakePanelLogLevelSwitch(), new RecordingAuditWriter());

        await handler.HandleAsync(new ChangeLogLevelCommand(PanelLogLevel.Warning), CancellationToken.None);

        var row = Assert.Single(await dbContext.LogLevelSettings.ToListAsync());
        Assert.Equal(LogLevelSetting.SingletonId, row.Id);
        Assert.Equal(PanelLogLevel.Warning, row.Level);
    }

    /// <summary>A second change replaces the stored level rather than adding a row.</summary>
    [Fact]
    public async Task A_second_change_replaces_the_stored_level_rather_than_adding_a_row()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var handler = CreateHandler(dbContext, new FakePanelLogLevelSwitch(), new RecordingAuditWriter());

        await handler.HandleAsync(new ChangeLogLevelCommand(PanelLogLevel.Warning), CancellationToken.None);
        await handler.HandleAsync(new ChangeLogLevelCommand(PanelLogLevel.Error), CancellationToken.None);

        var row = Assert.Single(await dbContext.LogLevelSettings.ToListAsync());
        Assert.Equal(PanelLogLevel.Error, row.Level);
    }

    /// <summary>The journal entry names the caller and the level before and after.</summary>
    [Fact]
    public async Task The_journal_entry_names_the_caller_and_the_level_before_and_after()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var audit = new RecordingAuditWriter();
        var handler = CreateHandler(dbContext, new FakePanelLogLevelSwitch(PanelLogLevel.Information), audit);

        await handler.HandleAsync(
            new ChangeLogLevelCommand(PanelLogLevel.Debug, "198.51.100.7", "browser"), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.PanelLogLevelChanged, entry.Action);
        Assert.Equal("Information -> Debug", entry.Subject);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("198.51.100.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Builds the handler over the given collaborators.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="levelSwitch">The switch the handler applies to.</param>
    /// <param name="audit">Where journal entries land.</param>
    /// <returns>The handler.</returns>
    private static ChangeLogLevelCommandHandler CreateHandler(
        Persistence.MonitoringDbContext dbContext,
        FakePanelLogLevelSwitch levelSwitch,
        RecordingAuditWriter audit)
    {
        return new ChangeLogLevelCommandHandler(
            dbContext,
            levelSwitch,
            new MonitoringAuditJournal(audit, new FakeCurrentUser()),
            new FakeClock());
    }
}
