using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Jobs;

/// <summary>
/// What the sweep does with a finished run's artifact (issue #134): it reads it back after every
/// completed run and not after a failed one, a finding mails the administrator once without
/// rewriting the run's own status, and a verification that could not run mails nobody.
/// </summary>
public sealed class BackupRunHandlerVerificationTests
{
    /// <summary>The first tick past the named hour, inside the tolerance: an on-time run.</summary>
    private static readonly DateTimeOffset OnTime = BackupSweepWorld.NamedHour.AddMinutes(1);

    /// <summary>A terminal event describing a finished archive.</summary>
    private static readonly BackupCreateEvent Created =
        new(BackupCreateEventKind.Created, 100, "done", 4096, new string('b', 64), 3, null);

    /// <summary>A terminal event describing a run the agent could not finish.</summary>
    private static readonly BackupCreateEvent Dropped =
        new(BackupCreateEventKind.Dropped, 40, "archiving", 0, string.Empty, 0, null);

    /// <summary>A completed scheduled run is verified once, for the account and backup it just wrote.</summary>
    [Fact]
    public async Task A_completed_scheduled_run_is_verified_and_recorded_verified()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var stored = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        var call = Assert.Single(world.Agent.Verifications);
        Assert.Equal(BackupSweepWorld.Username, call.AccountUsername);
        Assert.Equal(stored.Id.ToString(), call.BackupId);
        Assert.Equal(BackupVerificationState.Verified, stored.VerificationState);
        Assert.Empty(Mails(world));
    }

    /// <summary>A run that produced no artifact is never verified: there is nothing to read back.</summary>
    [Fact]
    public async Task A_failed_scheduled_run_is_not_verified()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Dropped]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Empty(world.Agent.Verifications);
        var stored = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupVerificationState.NotVerified, stored.VerificationState);
    }

    /// <summary>
    /// A finding about the artifact mails the administrator once, names the account and the failure
    /// in words, and leaves the run completed.
    /// </summary>
    [Fact]
    public async Task A_verification_failure_mails_once_and_does_not_rewrite_the_run_status()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);
        world.Agent.VerifyResult = Result<AgentBackupVerification>.Fail(
            Error.Of("AgentValidationFailed", ErrorType.Validation));

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var stored = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupStatus.Completed, stored.Status);
        Assert.Null(stored.RunFailureCode);
        Assert.Equal(BackupVerificationState.Failed, stored.VerificationState);
        Assert.Equal("BackupVerificationFailed", stored.VerificationFailureCode);

        var mail = Assert.Single(Mails(world));
        Assert.Equal(BackupSweepWorld.AlertRecipient, mail.Recipient);
        Assert.Contains(BackupSweepWorld.Username, mail.Subject, StringComparison.Ordinal);
        Assert.Contains(BackupSweepWorld.Username, mail.Body, StringComparison.Ordinal);

        var words = BackupsTestContext.FailureNames().Of("BackupVerificationFailed");
        Assert.Contains(words!, mail.Body, StringComparison.Ordinal);

        var entry = Assert.Single(
            world.Audit.Entries, candidate => { return candidate.Action == MonitoringAuditActions.AlertRaised; });
        Assert.Equal($"{BackupAlertKind.VerificationFailed}:{stored.Id}", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second sweep that finds nothing due mails nothing more about the same failure.</summary>
    [Fact]
    public async Task A_verification_failure_is_mailed_once_across_two_ticks()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);
        world.Agent.VerifyResult = Result<AgentBackupVerification>.Fail(
            Error.Of("AgentValidationFailed", ErrorType.Validation));

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);
        await world.At(OnTime.AddMinutes(5)).HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Single(Mails(world));
        Assert.Single(world.Agent.Verifications);
    }

    /// <summary>A verification the agent could not run leaves the row as it was and mails nobody.</summary>
    [Fact]
    public async Task A_verification_that_could_not_run_records_nothing_and_mails_nobody()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);
        world.Agent.VerifyResult = Result<AgentBackupVerification>.Fail(
            Error.Of("AgentSaturated", ErrorType.Failure));

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var stored = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupStatus.Completed, stored.Status);
        Assert.Equal(BackupVerificationState.NotVerified, stored.VerificationState);
        Assert.Null(stored.VerificationFailureCode);
        Assert.Empty(Mails(world));
        Assert.DoesNotContain(world.Audit.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
    }

    /// <summary>
    /// A verification that raises instead of answering never makes the run fail: the row stays
    /// completed, retention is still asked for, and no abort alert is sent for a run that succeeded.
    /// </summary>
    [Fact]
    public async Task A_verification_that_throws_does_not_fail_the_run()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);
        world.Agent.OnVerify = () => { throw new InvalidOperationException("socket dropped"); };

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(1, started);
        var stored = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupStatus.Completed, stored.Status);
        Assert.Null(stored.RunFailureCode);
        Assert.Single(world.Bus.Published.OfType<RetentionRequested>());
        Assert.Empty(Mails(world));
    }

    /// <summary>With no alert address configured the failed-verification alert is journalled as skipped.</summary>
    [Fact]
    public async Task With_no_alert_address_a_verification_failure_is_journalled_as_skipped()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(Guid.NewGuid(), null, OnTime, [Created]);
        world.Recipients = new StubAlertRecipientDirectory(null);
        world.At(OnTime);
        world.Agent.VerifyResult = Result<AgentBackupVerification>.Fail(
            Error.Of("AgentValidationFailed", ErrorType.Validation));

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Empty(Mails(world));
        var skipped = Assert.Single(
            world.Audit.Entries, entry => { return entry.Action == OutgoingMailAuditActions.MailSkippedNoSmtp; });
        Assert.True(skipped.Succeeded);
    }

    /// <summary>The alert mails the sweep published.</summary>
    /// <param name="world">The world the sweep ran in.</param>
    /// <returns>Every <see cref="SendMailRequested"/> on the bus, in order.</returns>
    private static List<SendMailRequested> Mails(BackupSweepWorld world)
    {
        return [.. world.Bus.Published.OfType<SendMailRequested>()];
    }
}
