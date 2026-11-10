using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.ServerMigration.Commands.StageAccountImport;

/// <summary>
/// Handles <see cref="StageAccountImportCommand"/>: refuses anyone but an administrator and stages the uploaded
/// package (issue #127).
/// </summary>
/// <remarks>
/// The strict read, the agent's own verification and the record are <see cref="AccountImportStager"/>, shared with
/// the pull from another panel so the two ways in cannot differ in what they check. What a success does not
/// establish, and why a trailing byte is a refusal after the agent has published, are stated there.
/// </remarks>
public sealed class StageAccountImportCommandHandler
{
    /// <summary>Stages a package.</summary>
    private readonly AccountImportStager _stager;

    /// <summary>Tells who is importing.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Records a refusal of a caller who is not an administrator.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="stager">Stages a package.</param>
    /// <param name="currentUser">Tells who is importing.</param>
    /// <param name="journal">Records a refusal of a caller who is not an administrator.</param>
    public StageAccountImportCommandHandler(
        AccountImportStager stager,
        ICurrentUser currentUser,
        ServerMigrationAuditJournal journal)
    {
        _stager = stager;
        _currentUser = currentUser;
        _journal = journal;
    }

    /// <summary>Stages the package.</summary>
    /// <param name="command">The upload and the confirmed fingerprint.</param>
    /// <param name="cancellationToken">Cancels the read and the relay.</param>
    /// <returns>The staged import, or the first refusal.</returns>
    public async Task<Result<AccountImportDto>> HandleAsync(
        StageAccountImportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_currentUser.Caller is not AdministratorCaller administrator)
        {
            await _journal.RecordFailureAsync(
                MigrationAuditActions.ImportStaged,
                command.ExpectedKeyFingerprint,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<AccountImportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound));
        }

        return await _stager.StageAsync(
            command.Package,
            command.ExpectedKeyFingerprint,
            administrator,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
