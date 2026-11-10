using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// This module's answer to the Host's grant handler: is the presented token a live grant for an export that can
/// still be served, from an address it allows (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>One answer for every refusal, and the same work.</b> Every path digests the presented value first and asks
/// the database by digest; a value of the wrong shape, an unknown digest, an expired, revoked or used grant, an
/// expired export and an address outside the restriction are all <see cref="MigrationGrantDenied"/>. The reason is
/// logged for the operator as a fixed phrase; the token, the digest and the address are not.
/// </para>
/// <para>
/// <b>It consumes nothing.</b> Reading the manifest and reading the artifact are counted by the pull command,
/// which knows which of the two it serves. A grant whose artifact was already read is refused here as well, so
/// a used grant opens nothing, not even the preview.
/// </para>
/// <para>
/// <b>Reads without a tenant scope on purpose</b>: this module's rows are administrator bookkeeping with no
/// query filter, and the caller here is nobody yet.
/// </para>
/// </remarks>
public sealed class MigrationGrantAuthenticator : IMigrationGrantAuthenticator
{
    /// <summary>Pre-compiled log delegate for a refused grant. Carries a fixed phrase and no value.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRefused =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(MigrationGrantAuthenticator)),
            "An export grant was refused: {Reason}");

    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Operator log.</summary>
    private readonly ILogger<MigrationGrantAuthenticator> _logger;

    /// <summary>Creates the authenticator.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="logger">Operator log.</param>
    public MigrationGrantAuthenticator(ServerMigrationDbContext dbContext, ILogger<MigrationGrantAuthenticator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MigrationGrantAuthentication> AuthenticateAsync(
        string token,
        string clientAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(clientAddress);

        // A token of the wrong length cannot be one this panel minted; it is refused before it is hashed, so a
        // header of kilobytes costs nothing.
        if (token.Length != ExportGrantTokenHasher.TokenLength)
        {
            return Refuse("the value is not the shape of a grant");
        }

        var digest = ExportGrantTokenHasher.Digest(token);
        var grant = await _dbContext.ExportGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.TokenDigest == digest, cancellationToken);

        if (grant is null || !ExportGrantTokenHasher.DigestsMatch(grant.TokenDigest, digest))
        {
            return Refuse("no grant has this digest");
        }

        if (!grant.IsLive(now))
        {
            return Refuse("the grant is revoked or expired");
        }

        if (grant.ArtifactConsumedAt is not null)
        {
            return Refuse("the grant's artifact was already read");
        }

        if (!AllowedFromPolicy.Admits(grant.AllowedFrom, clientAddress))
        {
            return Refuse("the caller is outside the grant's address restriction");
        }

        var exportAvailable = await _dbContext.AccountExports
            .AsNoTracking()
            .AnyAsync(row => row.Id == grant.ExportId && row.ExpiresAt > now, cancellationToken);

        return exportAvailable
            ? new MigrationGrantAuthenticated(grant.Id, grant.ExportId)
            : Refuse("the export has expired");
    }

    /// <summary>Logs a refusal and answers it.</summary>
    /// <param name="reason">A fixed phrase for the operator; never a value taken from the request.</param>
    /// <returns>The one denial.</returns>
    private MigrationGrantDenied Refuse(string reason)
    {
        LogRefused(_logger, reason, null);

        return MigrationGrantDenied.Instance;
    }
}
