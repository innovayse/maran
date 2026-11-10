using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Commands.PreviewAccountPull;

/// <summary>
/// Handles <see cref="PreviewAccountPullCommand"/>: reads the head of another panel's package and verifies it
/// against the fingerprint the administrator typed (issue #127).
/// </summary>
/// <remarks>
/// Counts as one of the grant's few manifest reads on the exporting side, and spends nothing else. The journal
/// names the other panel's host, never the grant. Only an administrator may ask: the panel makes an outbound
/// request for it.
/// </remarks>
public sealed class PreviewAccountPullCommandHandler
{
    /// <summary>Asks the other panel.</summary>
    private readonly IMigrationPullClient _pull;

    /// <summary>Tells who is asking.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Records the preview.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="pull">Asks the other panel.</param>
    /// <param name="currentUser">Tells who is asking.</param>
    /// <param name="journal">Records the preview.</param>
    public PreviewAccountPullCommandHandler(
        IMigrationPullClient pull,
        ICurrentUser currentUser,
        ServerMigrationAuditJournal journal)
    {
        _pull = pull;
        _currentUser = currentUser;
        _journal = journal;
    }

    /// <summary>Reads and verifies the head.</summary>
    /// <param name="command">The other panel, the grant and the confirmed fingerprint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the head says, or the first refusal.</returns>
    public async Task<Result<AccountPullPreviewDto>> HandleAsync(
        PreviewAccountPullCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = PublicUrlRule.TryNormalise(command.SourceUrl, out var origin);

        if (_currentUser.Caller is not AdministratorCaller)
        {
            return await FailAsync(command, origin, Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound), cancellationToken);
        }

        var opened = await _pull.OpenAsync(origin!, PullPart.Head, command.Grant, cancellationToken);

        if (!opened.IsSuccess)
        {
            return await FailAsync(command, origin, opened.Error!, cancellationToken);
        }

        await using var body = opened.Value!;
        Result<Models.VerifiedPackage> read;

        try
        {
            read = await MigrationPackageReader.ReadAsync(body, command.ExpectedKeyFingerprint, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or HttpRequestException)
        {
            return await FailAsync(command, origin, Error.Of(nameof(ErrorMessages.MigrationPullUnreachable), ErrorType.Unavailable), cancellationToken);
        }

        if (!read.IsSuccess)
        {
            return await FailAsync(command, origin, read.Error!, cancellationToken);
        }

        var verified = read.Value!;
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.ImportPullPreviewed, origin!, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountPullPreviewDto>.Ok(
            new AccountPullPreviewDto(
                verified.Header.AccountName,
                verified.Header.SourceHostName,
                verified.Header.KeyFingerprint,
                verified.Header.HomeBytes,
                verified.Manifest.ArtifactBytes,
                verified.Manifest.DatabaseCount,
                verified.Header.CreatedAt));
    }

    /// <summary>Journals a refusal and answers it.</summary>
    /// <param name="command">The command.</param>
    /// <param name="origin">The other panel's origin.</param>
    /// <param name="error">The refusal.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AccountPullPreviewDto>> FailAsync(
        PreviewAccountPullCommand command,
        string? origin,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.ImportPullPreviewed, origin ?? string.Empty, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountPullPreviewDto>.Fail(error);
    }
}
