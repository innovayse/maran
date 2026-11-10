using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>Runs the job of <see cref="AccountImportRequested"/> through the orchestrator (issue #127).</summary>
/// <remarks>
/// A handler with nothing in it but the call, on purpose: the orchestrator is the thing under test, and a
/// message handler cannot be reached from a test without the bus. The orchestrator never throws for a failed
/// import (it records the outcome on the row); an exception that does escape is an infrastructure fault and is
/// left to Wolverine.
/// </remarks>
public sealed class AccountImportRequestedHandler
{
    /// <summary>Runs one import.</summary>
    private readonly AccountImportOrchestrator _orchestrator;

    /// <summary>Creates the handler.</summary>
    /// <param name="orchestrator">Runs one import.</param>
    public AccountImportRequestedHandler(AccountImportOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>Runs the import named by the message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the job.</param>
    /// <returns>Resolves when the job has ended, however it ended.</returns>
    public async Task HandleAsync(AccountImportRequested message, CancellationToken cancellationToken)
    {
        await _orchestrator.RunAsync(message.ImportId, cancellationToken);
    }
}
