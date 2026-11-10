using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Commands.PullAccountImport;

/// <summary>
/// Handles <see cref="PullAccountImportCommand"/>: opens the whole package on the other panel and hands the stream
/// to the same stager an upload goes through (issue #127).
/// </summary>
/// <remarks>
/// Everything the stager checks is checked here, because it is the same code: the signatures, the confirmed
/// fingerprint, the signed length and the agent's own verification. A stall or a dropped connection mid-stream is
/// the unreachable failure, and the grant is spent either way (the exporting side takes its single read at the
/// first byte). The journal names the other panel's host, never the grant.
/// </remarks>
public sealed class PullAccountImportCommandHandler
{
    /// <summary>Asks the other panel.</summary>
    private readonly IMigrationPullClient _pull;

    /// <summary>Stages a package.</summary>
    private readonly AccountImportStager _stager;

    /// <summary>Tells who is importing.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Records the pull.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="pull">Asks the other panel.</param>
    /// <param name="stager">Stages a package.</param>
    /// <param name="currentUser">Tells who is importing.</param>
    /// <param name="journal">Records the pull.</param>
    public PullAccountImportCommandHandler(
        IMigrationPullClient pull,
        AccountImportStager stager,
        ICurrentUser currentUser,
        ServerMigrationAuditJournal journal)
    {
        _pull = pull;
        _stager = stager;
        _currentUser = currentUser;
        _journal = journal;
    }

    /// <summary>Pulls and stages the package.</summary>
    /// <param name="command">The other panel, the grant and the confirmed fingerprint.</param>
    /// <param name="cancellationToken">Cancels the request and the relay.</param>
    /// <returns>The staged import, or the first refusal.</returns>
    public async Task<Result<AccountImportDto>> HandleAsync(PullAccountImportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = PublicUrlRule.TryNormalise(command.SourceUrl, out var origin);

        if (_currentUser.Caller is not AdministratorCaller administrator)
        {
            return await FailAsync(command, origin, Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound), cancellationToken);
        }

        var opened = await _pull.OpenAsync(origin!, PullPart.Package, command.Grant, cancellationToken);

        if (!opened.IsSuccess)
        {
            return await FailAsync(command, origin, opened.Error!, cancellationToken);
        }

        await using var body = opened.Value!;
        Result<AccountImportDto> staged;

        try
        {
            staged = await _stager.StageAsync(
                body, command.ExpectedKeyFingerprint, administrator, command.IpAddress, command.UserAgent, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or HttpRequestException)
        {
            return await FailAsync(command, origin, Error.Of(nameof(ErrorMessages.MigrationPullUnreachable), ErrorType.Unavailable), cancellationToken);
        }

        await (staged.IsSuccess
            ? _journal.RecordSuccessAsync(MigrationAuditActions.ImportPulled, origin!, command.IpAddress, command.UserAgent, cancellationToken)
            : _journal.RecordFailureAsync(MigrationAuditActions.ImportPulled, origin!, command.IpAddress, command.UserAgent, cancellationToken));

        return staged;
    }

    /// <summary>Journals a refusal and answers it.</summary>
    /// <param name="command">The command.</param>
    /// <param name="origin">The other panel's origin.</param>
    /// <param name="error">The refusal.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AccountImportDto>> FailAsync(
        PullAccountImportCommand command,
        string? origin,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.ImportPulled, origin ?? string.Empty, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountImportDto>.Fail(error);
    }
}
