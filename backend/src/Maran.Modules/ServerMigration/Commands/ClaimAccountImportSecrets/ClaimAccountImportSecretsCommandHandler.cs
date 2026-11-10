using System.Text.Json;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Commands.ClaimAccountImportSecrets;

/// <summary>
/// Handles <see cref="ClaimAccountImportSecretsCommand"/>: reads the held credentials, clears them in the same
/// save, and hands them over (issue #127).
/// </summary>
/// <remarks>
/// A package carries no password, so an import generates new ones; they are held encrypted until the
/// administrator claims them, and the claim removes them. A second claim finds nothing and says so
/// (<c>MigrationImportNotFound</c> is deliberately not reused: that would read as a missing import). Nothing
/// here writes a credential to a log or a journal line.
/// </remarks>
public sealed class ClaimAccountImportSecretsCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ClaimAccountImportSecretsCommandHandler(ServerMigrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Claims the credentials.</summary>
    /// <param name="command">The import.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The credentials, once; or the refusal.</returns>
    public async Task<Result<IReadOnlyList<IssuedSecretDto>>> HandleAsync(
        ClaimAccountImportSecretsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var import = await _dbContext.AccountImports.FirstOrDefaultAsync(row => row.Id == command.ImportId, cancellationToken);

        if (import is null)
        {
            return Result<IReadOnlyList<IssuedSecretDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound));
        }

        var held = import.ClaimIssuedSecrets();

        if (held is null)
        {
            return Result<IReadOnlyList<IssuedSecretDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MigrationSecretsAlreadyClaimed), ErrorType.Conflict));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var secrets = JsonSerializer.Deserialize<List<StoredSecret>>(held, JsonSerializerOptions.Web) ?? [];

        return Result<IReadOnlyList<IssuedSecretDto>>.Ok(
            secrets.Select(secret =>
            {
                return new IssuedSecretDto(secret.Subject, new SensitiveString(secret.Secret));
            }).ToList());
    }

    /// <summary>The stored shape of one credential: plain text inside a column that is encrypted at rest.</summary>
    /// <param name="Subject">What it opens.</param>
    /// <param name="Secret">The credential.</param>
    private sealed record StoredSecret(string Subject, string Secret);
}
