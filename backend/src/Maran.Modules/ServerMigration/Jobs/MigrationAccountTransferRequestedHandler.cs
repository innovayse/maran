using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>Runs the pass of <see cref="MigrationAccountTransferRequested"/> through the engine (issue #197, phase 2).</summary>
/// <remarks>
/// A handler with nothing in it but the call, for the reason <see cref="AccountImportRequestedHandler"/> states. The
/// engine records the outcome of a failed pass on the row and never throws for it; an exception that escapes is an
/// infrastructure fault.
/// </remarks>
public sealed class MigrationAccountTransferRequestedHandler
{
    /// <summary>Runs one pass.</summary>
    private readonly MigrationAccountTransfer _transfer;

    /// <summary>Creates the handler.</summary>
    /// <param name="transfer">Runs one pass.</param>
    public MigrationAccountTransferRequestedHandler(MigrationAccountTransfer transfer)
    {
        _transfer = transfer;
    }

    /// <summary>Runs the pass the message names.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The host's shutdown token.</param>
    /// <returns>Resolves when the pass has ended, however it ended.</returns>
    public async Task HandleAsync(MigrationAccountTransferRequested message, CancellationToken cancellationToken)
    {
        await _transfer.RunAsync(message.AccountId, message.Kind, cancellationToken);
    }
}
