using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// The strict read of an untrusted package, then the agent's own verification, then a record (issue #127). The
/// one implementation of staging, used for an uploaded package and for one pulled from another panel, so the two
/// ways in can never differ in what they check.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two independent checks, in series.</b> This verifies the signatures and the confirmed fingerprint before it
/// forwards a single artifact byte; the agent then verifies them again over the same bytes, checks the declared
/// length and SHA-256 while it receives the stream, scans the archive, and publishes it only if the account name
/// is free on this host. A refusal of either is a refusal, and the agent's reason is carried to the administrator.
/// </para>
/// <para>
/// <b>What a success does not establish.</b> That the SQL loads or that a restore will succeed. A valid
/// signature proves only that the holder of the key signed, and whether the holder is the server the
/// administrator believes it is rests on the fingerprint they compared.
/// </para>
/// <para>
/// A package that carries bytes after the signed length is refused as malformed. The agent has by then
/// published the artifact it was sent, with no panel record naming it: that stage is not reachable from any
/// screen and is the agent's to reclaim.
/// </para>
/// </remarks>
public sealed class AccountImportStager
{
    /// <summary>The size of the slices relayed to the agent.</summary>
    private const int ChunkBytes = 1024 * 1024;

    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The agent's transfer client.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the import.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Names a stored failure in the caller's language.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Creates the stager.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="agent">The agent's transfer client.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the import.</param>
    /// <param name="failureNames">Names a stored failure in the caller's language.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public AccountImportStager(
        ServerMigrationDbContext dbContext,
        IAgentTransferClient agent,
        IClock clock,
        ServerMigrationAuditJournal journal,
        AccountImportFailureNames failureNames,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _agent = agent;
        _clock = clock;
        _journal = journal;
        _failureNames = failureNames;
        _names = names;
    }

    /// <summary>Stages a package.</summary>
    /// <param name="package">The package, positioned at its first byte; read forward and never buffered.</param>
    /// <param name="expectedKeyFingerprint">The fingerprint of the sending server's key that the administrator confirmed.</param>
    /// <param name="administrator">The administrator importing.</param>
    /// <param name="ipAddress">The caller's address, for the journal.</param>
    /// <param name="userAgent">The caller's user agent, for the journal.</param>
    /// <param name="cancellationToken">Cancels the read and the relay.</param>
    /// <returns>The staged import, or the first refusal.</returns>
    public async Task<Result<AccountImportDto>> StageAsync(
        Stream package,
        string expectedKeyFingerprint,
        AdministratorCaller administrator,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(administrator);

        var read = await MigrationPackageReader.ReadAsync(package, expectedKeyFingerprint, cancellationToken);

        if (!read.IsSuccess)
        {
            return await FailAsync(expectedKeyFingerprint, ipAddress, userAgent, read.Error!, cancellationToken);
        }

        var verified = read.Value!;
        var artifact = new BoundedReadStream(package, verified.Manifest.ArtifactBytes);
        var imported = await _agent.ImportBackupAsync(
            new AgentBackupImportHeader(
                verified.ManifestBytes,
                verified.ManifestSignature,
                verified.PublicKey,
                expectedKeyFingerprint,
                MigrationPackageLimits.MaxArtifactBytes),
            ChunksAsync(artifact, cancellationToken),
            cancellationToken);

        if (imported is not AgentBackupImported published)
        {
            return await FailAsync(
                expectedKeyFingerprint, ipAddress, userAgent, ToError((AgentBackupImportRefused)imported), cancellationToken);
        }

        if (await MigrationPackageReader.HasTrailingBytesAsync(package, cancellationToken)
            || !Guid.TryParse(published.BackupId, out var backupId)
            || backupId != verified.Manifest.BackupId)
        {
            return await FailAsync(
                expectedKeyFingerprint,
                ipAddress,
                userAgent,
                Error.Of(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation),
                cancellationToken);
        }

        var import = new AccountImport(
            Guid.NewGuid(),
            verified.Header.AccountName,
            PrimaryDomainOf(verified.Header.Sections),
            verified.Header.SourceHostName,
            verified.Header.KeyFingerprint,
            verified.SectionsJson,
            verified.Header.HomeBytes,
            new MigrationImportedArtifact(backupId, published.ArtifactBytes, published.ArtifactSha256, published.DatabaseCount),
            administrator.UserId,
            _clock.UtcNow);
        _dbContext.AccountImports.Add(import);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.ImportStaged,
            import.SourceAccountName,
            ipAddress,
            userAgent,
            cancellationToken);

        return Result<AccountImportDto>.Ok(AccountImportMapper.ToDto(import, _failureNames.Of(import.FailureCode), _names));
    }

    /// <summary>Streams a bounded artifact in slices.</summary>
    /// <param name="artifact">The artifact, bounded to its signed length.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The slices, none empty.</returns>
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ChunksAsync(
        Stream artifact,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkBytes];

        while (true)
        {
            var read = await artifact.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                yield break;
            }

            yield return buffer.AsMemory(0, read).ToArray();
        }
    }

    /// <summary>Turns the agent's refusal into the code the administrator is told.</summary>
    /// <param name="refused">The agent's refusal.</param>
    /// <returns>The error: the reason when the agent gave one, otherwise the agent's own code.</returns>
    private static Error ToError(AgentBackupImportRefused refused)
    {
        return refused.Reason switch
        {
            BackupImportRefusal.SignatureInvalid => Error.Of(nameof(ErrorMessages.MigrationSignatureInvalid), ErrorType.Validation),
            BackupImportRefusal.FingerprintMismatch => Error.Of(nameof(ErrorMessages.MigrationFingerprintMismatch), ErrorType.Conflict),
            BackupImportRefusal.AccountExists => Error.Of(MigrationCodes.NameTaken, ErrorType.Conflict),
            BackupImportRefusal.BackupIdTaken => Error.Of(MigrationCodes.BackupIdTaken, ErrorType.Conflict),
            BackupImportRefusal.TooLarge => Error.Of(nameof(ErrorMessages.MigrationPackageTooLarge), ErrorType.Validation),
            BackupImportRefusal.ManifestMalformed
                or BackupImportRefusal.LengthMismatch
                or BackupImportRefusal.DigestMismatch
                or BackupImportRefusal.ArtifactRefused => Error.Of(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation),
            _ => refused.Error,
        };
    }

    /// <summary>Reads the account's primary domain out of the Accounts section, for display only.</summary>
    /// <param name="sections">The package's sections.</param>
    /// <returns>The domain, or empty when the section is absent or unreadable; nothing acts on this value.</returns>
    private static string PrimaryDomainOf(IReadOnlyList<MigrationSection> sections)
    {
        var accounts = sections.FirstOrDefault(section =>
        {
            return section.ModuleId == "accounts";
        });

        if (accounts is null)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(accounts.Json);

            return document.RootElement.TryGetProperty("primaryDomain", out var domain)
                ? ColumnText.Fit(domain.GetString() ?? string.Empty, AccountImport.NameMaxLength)
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Journals a refusal and answers it.</summary>
    /// <param name="subject">What the entry names: the fingerprint the administrator confirmed.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="error">The refusal.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AccountImportDto>> FailAsync(
        string subject,
        string ipAddress,
        string userAgent,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(MigrationAuditActions.ImportStaged, subject, ipAddress, userAgent, cancellationToken);

        return Result<AccountImportDto>.Fail(error);
    }
}
