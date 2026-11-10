using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.ServerMigration.Commands.CreateAccountExport;

/// <summary>
/// Handles <see cref="CreateAccountExportCommand"/>: takes the backup the package is made from, asks every
/// module for its section, and records the export (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing leaves this server here.</b> The export is a record and a backup; the package is written when it
/// is downloaded or pulled. What each module contributes is an allow-list of named fields, so a secret column
/// added to an entity later is not exported by accident.
/// </para>
/// <para>
/// A panel composed without Backups has nothing to export from and says so (<c>MigrationBackupsUnavailable</c>)
/// instead of recording an export with no artifact. The account is read through the tenant-scoped directory, so
/// an account the caller may not see is the same answer as one that does not exist.
/// </para>
/// </remarks>
public sealed class CreateAccountExportCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Reads the account.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The Backups module's window, absent when the panel has no Backups module.</summary>
    private readonly IMigrationArtifactService? _artifacts;

    /// <summary>Every module's exporter.</summary>
    private readonly IReadOnlyList<IAccountMigrationExporter> _exporters;

    /// <summary>This server's signing key.</summary>
    private readonly MigrationSigningKeys _keys;

    /// <summary>Tells who is exporting.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the export.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accounts">Reads the account.</param>
    /// <param name="artifacts">The Backups module's window; a collection, because an absent module registers nothing and an optional parameter cannot say so.</param>
    /// <param name="exporters">Every module's exporter.</param>
    /// <param name="keys">This server's signing key.</param>
    /// <param name="currentUser">Tells who is exporting.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the export.</param>
    public CreateAccountExportCommandHandler(
        ServerMigrationDbContext dbContext,
        IAccountDirectory accounts,
        IEnumerable<IMigrationArtifactService> artifacts,
        IEnumerable<IAccountMigrationExporter> exporters,
        MigrationSigningKeys keys,
        ICurrentUser currentUser,
        IClock clock,
        ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _artifacts = artifacts.FirstOrDefault();
        _exporters = exporters.ToList();
        _keys = keys;
        _currentUser = currentUser;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Exports the account.</summary>
    /// <param name="command">The account to export.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The export, or the typed failure.</returns>
    public async Task<Result<AccountExportDto>> HandleAsync(
        CreateAccountExportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_currentUser.Caller is not AdministratorCaller administrator)
        {
            return await FailAsync(command, command.AccountId.ToString(), ErrorType.NotFound, nameof(ErrorMessages.MigrationAccountNotFound), cancellationToken);
        }

        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);

        if (account is null)
        {
            return await FailAsync(command, command.AccountId.ToString(), ErrorType.NotFound, nameof(ErrorMessages.MigrationAccountNotFound), cancellationToken);
        }

        if (_artifacts is null)
        {
            return await FailAsync(command, account.Username, ErrorType.Conflict, nameof(ErrorMessages.MigrationBackupsUnavailable), cancellationToken);
        }

        var artifact = await _artifacts.CreateForExportAsync(account.Id, account.Username, cancellationToken);

        if (!artifact.IsSuccess)
        {
            return await FailAsync(command, account.Username, artifact.Error!.Type, artifact.Error.Code, cancellationToken);
        }

        var sections = new List<MigrationSection>();

        foreach (var exporter in _exporters)
        {
            var section = await exporter.ExportAsync(account.Id, cancellationToken);

            if (section is not null)
            {
                sections.Add(section);
            }
        }

        var export = new AccountExport(
            Guid.NewGuid(),
            account.Id,
            account.Username,
            artifact.Value!,
            JsonSerializer.Serialize(sections, JsonSerializerOptions.Web),
            administrator.UserId,
            _clock.UtcNow,
            MigrationPackageLimits.ExportRetention);
        _dbContext.AccountExports.Add(export);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var key = await _keys.GetOrCreateAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.AccountExported,
            account.Username,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<AccountExportDto>.Ok(AccountImportMapper.ToDto(export, key.Fingerprint));
    }

    /// <summary>Journals a refusal and answers it.</summary>
    /// <param name="command">The command.</param>
    /// <param name="subject">What the refusal is about.</param>
    /// <param name="type">The kind of failure.</param>
    /// <param name="code">The error code.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AccountExportDto>> FailAsync(
        CreateAccountExportCommand command,
        string subject,
        ErrorType type,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.AccountExported,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<AccountExportDto>.Fail(Error.Of(code, type));
    }
}
