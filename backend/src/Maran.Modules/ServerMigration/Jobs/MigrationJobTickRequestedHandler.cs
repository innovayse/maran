using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>Runs the scheduler for the job of <see cref="MigrationJobTickRequested"/> (issue #197, phase 2).</summary>
public sealed class MigrationJobTickRequestedHandler
{
    /// <summary>The scheduler.</summary>
    private readonly MigrationJobScheduler _scheduler;

    /// <summary>Creates the handler.</summary>
    /// <param name="scheduler">The scheduler.</param>
    public MigrationJobTickRequestedHandler(MigrationJobScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>Looks at the job the message names.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The host's shutdown token.</param>
    /// <returns>Resolves when the scheduler has decided.</returns>
    public async Task HandleAsync(MigrationJobTickRequested message, CancellationToken cancellationToken)
    {
        await _scheduler.TickAsync(message.JobId, cancellationToken);
    }
}
