using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Turns the failure of a pull of files or of a database transfer into the code the panel records on the account
/// and shows the administrator (issue #197, phase 2).
/// </summary>
/// <remarks>
/// The agent's own text is never carried: it is logged at the client boundary, and an <see cref="Error"/> holds a
/// code and nothing else. A transfer reason is a more specific answer than a connection reason (a refused dump was
/// refused after the connection worked), so it is read first; a failure with neither is the connection translation of
/// <see cref="MigrationAgentErrorTranslator"/>.
/// </remarks>
public static class MigrationTransferErrorTranslator
{
    /// <summary>Translates a failure.</summary>
    /// <param name="agentError">The agent client's typed error.</param>
    /// <param name="reason">Which connection failure it was.</param>
    /// <param name="transferReason">Which failure of the transfer it was.</param>
    /// <returns>The error to record.</returns>
    public static Error Translate(Error agentError, SourceFailureReason reason, TransferFailureReason transferReason)
    {
        ArgumentNullException.ThrowIfNull(agentError);

        return transferReason switch
        {
            TransferFailureReason.DiskInsufficient =>
                Error.Of(nameof(ErrorMessages.MigrationDiskInsufficient), ErrorType.Conflict),
            TransferFailureReason.PullFailed =>
                Error.Of(nameof(ErrorMessages.MigrationPullFailed), ErrorType.Failure),
            TransferFailureReason.PullIncomplete =>
                Error.Of(nameof(ErrorMessages.MigrationPullIncomplete), ErrorType.Failure),
            TransferFailureReason.DumpFailed =>
                Error.Of(nameof(ErrorMessages.MigrationDumpFailed), ErrorType.Failure),
            TransferFailureReason.DumpTooLarge =>
                Error.Of(nameof(ErrorMessages.MigrationDumpTooLarge), ErrorType.Conflict),
            TransferFailureReason.LoadRefused =>
                Error.Of(nameof(ErrorMessages.MigrationDumpRefused), ErrorType.Conflict),
            TransferFailureReason.LoadFailed =>
                Error.Of(nameof(ErrorMessages.MigrationDumpLoadFailed), ErrorType.Failure),
            _ => reason == SourceFailureReason.Unspecified && agentError.Type != ErrorType.Unavailable
                ? Error.Of(nameof(ErrorMessages.MigrationTransferFaulted), ErrorType.Failure)
                : MigrationAgentErrorTranslator.Translate(agentError, reason),
        };
    }
}
