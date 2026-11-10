using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Commands.ClaimMigrationAccountSecrets;

/// <summary>
/// Handles <see cref="ClaimMigrationAccountSecretsCommand"/>: hands the generated credentials over once and forgets
/// them (issue #197, phase 2). The same promise as an import's: a package or a source carries no password, so the
/// databases got new ones, shown here once and kept by nobody.
/// </summary>
public sealed class ClaimMigrationAccountSecretsCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="journal">This module's audit journal.</param>
    public ClaimMigrationAccountSecretsCommandHandler(ServerMigrationDbContext dbContext, ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Claims the credentials.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The credentials; <c>MigrationJobAccountNotFound</c> or <c>MigrationSecretsAlreadyClaimed</c> otherwise.</returns>
    public async Task<Result<IReadOnlyList<IssuedSecretDto>>> HandleAsync(
        ClaimMigrationAccountSecretsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await _dbContext.FindAccountAsync(command.JobId, command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<IReadOnlyList<IssuedSecretDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationJobAccountNotFound), ErrorType.NotFound));
        }

        var held = account.ClaimIssuedSecrets();
        if (held is null)
        {
            return Result<IReadOnlyList<IssuedSecretDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationSecretsAlreadyClaimed), ErrorType.Conflict));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.AccountSecretsClaimed, account.SourceAccountName, string.Empty, string.Empty, cancellationToken);

        var secrets = JsonSerializer.Deserialize<List<StoredSecret>>(held, JsonSerializerOptions.Web) ?? [];

        return Result<IReadOnlyList<IssuedSecretDto>>.Ok(
            secrets.Select(secret =>
            {
                return new IssuedSecretDto(secret.Subject, new SensitiveString(secret.Secret));
            }).ToList());
    }

    /// <summary>One credential as it is stored.</summary>
    /// <param name="Subject">What it opens.</param>
    /// <param name="Secret">The credential.</param>
    private sealed record StoredSecret(string Subject, string Secret);
}
