using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Jobs;

/// <summary>
/// The sweep's operator alerts (issue #134): a scheduled run that failed, one whose destination was
/// refused, and one that missed its hour each mail the administrator once — and a run that went
/// right mails nobody.
/// </summary>
public sealed class BackupRunHandlerAlertTests
{
    /// <summary>The first tick past the named hour, inside the tolerance: an on-time run.</summary>
    private static readonly DateTimeOffset OnTime = BackupSweepWorld.NamedHour.AddMinutes(1);

    /// <summary>Two hours past the named hour: the panel was not running at 03:00.</summary>
    private static readonly DateTimeOffset Late = BackupSweepWorld.NamedHour.AddHours(2);

    /// <summary>A terminal event describing a finished archive.</summary>
    private static readonly BackupCreateEvent Created =
        new(BackupCreateEventKind.Created, 100, "done", 4096, new string('b', 64), 3, null);

    /// <summary>A terminal event describing a run the agent could not finish.</summary>
    private static readonly BackupCreateEvent Dropped =
        new(BackupCreateEventKind.Dropped, 40, "archiving", 0, string.Empty, 0, null);

    /// <summary>An on-time run that completed sends no alert and journals none.</summary>
    /// <remarks>The inverse control for every case below: a mailer that always fires fails here.</remarks>
    [Fact]
    public async Task An_on_time_completed_run_alerts_nobody()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Empty(Mails(world));
        Assert.DoesNotContain(world.Audit.Entries, entry => { return entry.Action == AuditActions.AlertRaised; });
    }

    /// <summary>A scheduled run the agent could not finish mails the administrator once.</summary>
    [Fact]
    public async Task A_failed_scheduled_run_mails_the_administrator()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Dropped]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var mail = Assert.Single(Mails(world));
        Assert.Equal(BackupSweepWorld.AlertRecipient, mail.Recipient);
        Assert.Contains(BackupSweepWorld.Username, mail.Subject, StringComparison.Ordinal);
        AssertAlertJournalled(world, $"{BackupAlertKind.ScheduledRunFailed}:{BackupSweepWorld.Username}");
    }

    /// <summary>A run that threw before reporting an outcome is a failed run and mails as one.</summary>
    [Fact]
    public async Task An_aborted_scheduled_run_mails_the_administrator()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);
        world.Agent.OnCreate = () => { throw new InvalidOperationException("socket dropped"); };

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var mail = Assert.Single(Mails(world));
        Assert.Contains("BackupScheduledRunAborted", mail.Body, StringComparison.Ordinal);
        AssertAlertJournalled(world, $"{BackupAlertKind.ScheduledRunFailed}:{BackupSweepWorld.Username}");
    }

    /// <summary>A destination the panel cannot use mails the administrator and starts nothing.</summary>
    /// <remarks>
    /// The unreachable-target case the issue names, as the panel can observe it today: the schedule
    /// points at a destination that does not resolve, so no run reaches the agent at all.
    /// </remarks>
    [Fact]
    public async Task A_refused_destination_mails_the_administrator()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(
            Guid.NewGuid(), null, OnTime, [Created], destinationId: Guid.NewGuid());

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Empty(world.Agent.Creates);
        var mail = Assert.Single(Mails(world));
        Assert.Contains(BackupSweepWorld.Username, mail.Subject, StringComparison.Ordinal);
        AssertAlertJournalled(world, $"{BackupAlertKind.DestinationRefused}:{BackupSweepWorld.Username}");
    }

    /// <summary>A run that starts long after its named hour mails once, and the next tick does not repeat it.</summary>
    [Fact]
    public async Task A_missed_hour_mails_once_across_two_ticks()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, Late, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);
        await world.At(Late.AddMinutes(5)).HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var mail = Assert.Single(Mails(world));
        Assert.Contains("2026-01-02 03:00", mail.Body, StringComparison.Ordinal);
        Assert.Contains(BackupSweepWorld.Username, mail.Body, StringComparison.Ordinal);

        var schedule = Assert.Single(world.Read().BackupSchedules.IgnoreQueryFilters());
        AssertAlertJournalled(world, $"{BackupAlertKind.ScheduledRunMissed}:{schedule.Id}");
        Assert.Single(world.Agent.Creates);
    }

    /// <summary>With no alert address configured, the alert is journalled as skipped and nothing published.</summary>
    [Fact]
    public async Task With_no_alert_address_the_alert_is_journalled_as_skipped()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Dropped]);
        world.Recipients = new StubAlertRecipientDirectory(null);
        world.At(OnTime);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Empty(Mails(world));
        var skipped = Assert.Single(
            world.Audit.Entries, entry => { return entry.Action == AuditActions.MailSkippedNoSmtp; });
        Assert.True(skipped.Succeeded);
    }

    /// <summary>The alert mails the sweep published.</summary>
    /// <param name="world">The world the sweep ran in.</param>
    /// <returns>Every <see cref="SendMailRequested"/> on the bus, in order.</returns>
    private static List<SendMailRequested> Mails(BackupSweepWorld world)
    {
        return [.. world.Bus.Published.OfType<SendMailRequested>()];
    }

    /// <summary>Asserts exactly one raised alert was journalled, with the given subject, as the panel.</summary>
    /// <param name="world">The world the sweep ran in.</param>
    /// <param name="subject">The <c>kind:subject</c> the entry must carry.</param>
    private static void AssertAlertJournalled(BackupSweepWorld world, string subject)
    {
        var entry = Assert.Single(
            world.Audit.Entries, entry => { return entry.Action == AuditActions.AlertRaised; });
        Assert.Equal(subject, entry.Subject);
        Assert.True(entry.Succeeded);
    }
}
