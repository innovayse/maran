using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.ServerMigration.Commands.ControlMigrationJob;

/// <summary>
/// Handles <see cref="ControlMigrationJobCommand"/>: the four things an administrator does to a whole job (issue
/// #197, phase 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Start judges again.</b> The dry run is advice; the start runs the same planner and refuses while any account has
/// a conflict, because the state of this server can change between the report and the click.
/// </para>
/// <para>
/// <b>Pause stops, it does not discard.</b> Every worker is told to stop at its next boundary and keeps its partial
/// state; an account the database calls running with no worker in this process (the process restarted) is paused too,
/// which is how such an orphan is recovered: pause, then resume.
/// </para>
/// <para>
/// <b>Cancel reclaims.</b> The accounts the job created are deleted again (a completed account is left alone), through
/// <see cref="MigrationAccountCanceller"/>.
/// </para>
/// </remarks>
public sealed class ControlMigrationJobCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The dry run, repeated at the start.</summary>
    private readonly MigrationJobPlanner _planner;

    /// <summary>What this process runs.</summary>
    private readonly MigrationRunRegistry _registry;

    /// <summary>Ends accounts the administrator cancels.</summary>
    private readonly MigrationAccountCanceller _canceller;

    /// <summary>Sends the tick.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="planner">The dry run.</param>
    /// <param name="registry">What this process runs.</param>
    /// <param name="canceller">Ends accounts the administrator cancels.</param>
    /// <param name="bus">Sends the tick.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public ControlMigrationJobCommandHandler(
        ServerMigrationDbContext dbContext,
        MigrationJobPlanner planner,
        MigrationRunRegistry registry,
        MigrationAccountCanceller canceller,
        IMessageBus bus,
        IClock clock,
        ServerMigrationAuditJournal journal,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _planner = planner;
        _registry = registry;
        _canceller = canceller;
        _bus = bus;
        _clock = clock;
        _journal = journal;
        _names = names;
    }

    /// <summary>Does what was asked.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The job after the change; <c>MigrationJobNotFound</c>, <c>MigrationJobStateInvalid</c>, <c>MigrationJobNoAccounts</c> or <c>MigrationJobHasConflicts</c> otherwise.</returns>
    public async Task<Result<MigrationJobDto>> HandleAsync(
        ControlMigrationJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await _dbContext.MigrationJobs.FirstOrDefaultAsync(row => row.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result<MigrationJobDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var accounts = await _dbContext.MigrationJobAccounts.Where(row => row.JobId == job.Id).ToListAsync(cancellationToken);
        var changed = command.Action switch
        {
            MigrationJobAction.Start => await StartAsync(job, accounts, cancellationToken),
            MigrationJobAction.Pause => Pause(job, accounts),
            MigrationJobAction.Resume => Resume(job, accounts),
            _ => await CancelAsync(job, accounts, cancellationToken),
        };
        if (!changed.IsSuccess)
        {
            return Result<MigrationJobDto>.Fail(changed.Error!);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(AuditActionOf(command.Action), job.Name, command.IpAddress, command.UserAgent, cancellationToken);
        if (command.Action is MigrationJobAction.Start or MigrationJobAction.Resume)
        {
            await _bus.PublishAsync(new MigrationJobTickRequested(job.Id));
        }

        return Result<MigrationJobDto>.Ok(
            MigrationJobMapper.ToDto(job, await _dbContext.CountStatesAsync(job.Id, cancellationToken), _names));
    }

    /// <summary>Starts a draft job after judging it again.</summary>
    /// <returns>Success, or the refusal.</returns>
    private async Task<Result> StartAsync(MigrationJob job, List<MigrationJobAccount> accounts, CancellationToken cancellationToken)
    {
        if (!job.IsEditable)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationJobStateInvalid), ErrorType.Conflict));
        }

        if (accounts.Count == 0)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNoAccounts), ErrorType.Validation));
        }

        var findings = await _planner.PlanAsync(accounts, cancellationToken);
        if (findings.Any(row =>
        {
            return row.Finding.Severity == MigrationFindingSeverity.Conflict;
        }))
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationJobHasConflicts), ErrorType.Conflict));
        }

        return job.Start(_clock.UtcNow);
    }

    /// <summary>Pauses a running job and stops its workers; an orphan with no worker is paused too.</summary>
    /// <returns>Success, or the refusal.</returns>
    private Result Pause(MigrationJob job, List<MigrationJobAccount> accounts)
    {
        var paused = job.Pause();
        if (!paused.IsSuccess)
        {
            return paused;
        }

        foreach (var account in accounts.Where(row =>
        {
            return row.State is MigrationAccountState.Running or MigrationAccountState.CuttingOver;
        }))
        {
            if (_registry.IsRunning(account.Id))
            {
                _registry.Stop(account.Id, MigrationStopReason.Pause);
            }
            else
            {
                account.Paused();
            }
        }

        return Result.Ok();
    }

    /// <summary>Resumes a paused job: paused accounts wait again.</summary>
    /// <returns>Success, or the refusal.</returns>
    private static Result Resume(MigrationJob job, List<MigrationJobAccount> accounts)
    {
        var resumed = job.Resume();
        if (!resumed.IsSuccess)
        {
            return resumed;
        }

        foreach (var account in accounts.Where(row =>
        {
            return row.State == MigrationAccountState.Paused;
        }))
        {
            account.Requeue();
        }

        return Result.Ok();
    }

    /// <summary>Cancels the job and every account that is not completed.</summary>
    /// <returns>Success, or the refusal.</returns>
    private async Task<Result> CancelAsync(MigrationJob job, List<MigrationJobAccount> accounts, CancellationToken cancellationToken)
    {
        var cancelled = job.Cancel(_clock.UtcNow);
        if (!cancelled.IsSuccess)
        {
            return cancelled;
        }

        foreach (var account in accounts.Where(row =>
        {
            return !row.HasEnded;
        }))
        {
            // A failed deletion is recorded on the row and the job is still cancelled: the row names the account.
            await _canceller.CancelAsync(account, cancellationToken);
        }

        return Result.Ok();
    }

    /// <summary>The audit action of a job action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The action's name.</returns>
    private static string AuditActionOf(MigrationJobAction action)
    {
        return action switch
        {
            MigrationJobAction.Start => MigrationAuditActions.JobStarted,
            MigrationJobAction.Pause => MigrationAuditActions.JobPaused,
            MigrationJobAction.Resume => MigrationAuditActions.JobResumed,
            _ => MigrationAuditActions.JobCancelled,
        };
    }
}
