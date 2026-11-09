using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Persistence;

namespace Maran.Modules.Databases.Seeders;

/// <summary>
/// Gives every database that has no user row its dedicated <see cref="DatabaseUser"/>, holding the
/// full-access grant the database was created with.
/// </summary>
/// <remarks>
/// <para>
/// A start-up pass and not migration SQL, on purpose. The previous release creates databases with no
/// user row, so an installer roll-back followed by a roll-forward leaves databases a one-shot migration
/// would never see again. This pass is idempotent and converges on every start: it creates only what is
/// missing and touches nothing that exists.
/// </para>
/// <para>
/// It runs with no principal, so the anonymous caller's tenant filter shows it nothing; it reads every
/// account's rows with <c>IgnoreQueryFilters</c>, which is the deliberate, reasoned bypass
/// rules/csharp.md allows for unattended work.
/// </para>
/// <para>
/// The full-access grant is what <c>CreateDatabase</c> gives a dedicated user on the server, so the row
/// states a fact that is already true there. A customer who changed a user's privileges through the
/// previous release's tools has no row to lose: this panel had no such tool.
/// </para>
/// </remarks>
public sealed class DatabaseUserBackfillSeeder
{
    /// <summary>The module's database context.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seeder.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The injected time source, which stamps the created rows.</param>
    public DatabaseUserBackfillSeeder(DatabasesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Writes the missing dedicated-user rows.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many users were created; zero when every database already had its row.</returns>
    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // start-up work with no principal: it must read every account's databases
        var databases = await _dbContext.Databases
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

#pragma warning disable RS0030 // start-up work with no principal: it must read every account's user rows
        var existing = await _dbContext.DatabaseUsers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(user => new { user.AccountId, user.NameSuffix })
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        var known = existing
            .Select(user =>
            {
                return (user.AccountId, user.NameSuffix);
            })
            .ToHashSet();

        var created = 0;

        foreach (var database in databases)
        {
            if (!known.Add((database.AccountId, database.DbUserNameSuffix)))
            {
                continue;
            }

            var now = _clock.UtcNow;
            var user = new DatabaseUser(
                Guid.NewGuid(), database.AccountId, database.DbUserNameSuffix, database.DbUserName, now);
            user.ReplacePrivileges(database.Id, DatabasePrivilegeSets.Full, Guid.NewGuid(), now);

            _dbContext.DatabaseUsers.Add(user);
            created++;
        }

        if (created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return created;
    }
}
