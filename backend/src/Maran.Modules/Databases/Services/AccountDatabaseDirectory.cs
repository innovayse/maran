using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// This module's implementation of <see cref="IAccountDatabaseDirectory"/> — the only window another
/// module has onto the <c>databases</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// The scope is left to <see cref="DatabasesDbContext"/>'s global query filter rather than repeated
/// as a <c>Where</c> clause, so the scope another module gets is the same scope this module gets,
/// from the same code. There is no unscoped counterpart here and there must not be one added
/// casually: <see cref="ISiteDirectory"/> has one because certificate renewal genuinely runs for
/// nobody, and no such caller exists for a list of database names.
/// </para>
/// <para>
/// <b>It projects the FULL name, which is what the agent addresses a database by.</b>
/// <see cref="Domain.Entities.Database.Name"/> is the suffix the customer typed and means nothing to
/// MySQL on its own; <see cref="Domain.Entities.Database.FullName"/> is what the server holds, as the
/// agent itself reported it when the database was created. Handing over the suffix would produce an
/// allowed-list that matched no manifest entry, and a restore whose every database was refused —
/// which is a failure that looks exactly like a corrupt archive.
/// </para>
/// </remarks>
public sealed class AccountDatabaseDirectory : IAccountDatabaseDirectory
{
    /// <summary>The Databases module's context, carrying the caller's tenant scope.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the directory over this module's context.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    public AccountDatabaseDirectory(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListNamesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _dbContext.Databases
            .AsNoTracking()
            .Where(database => database.AccountId == accountId)
            .Select(database => database.FullName)
            .ToListAsync(cancellationToken);
    }
}
