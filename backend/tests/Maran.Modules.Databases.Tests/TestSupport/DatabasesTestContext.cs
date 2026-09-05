using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="DatabasesDbContext"/> instances for a named tenant, plus the rows to
/// seed them with. Each context gets its own uniquely-named in-memory database unless a caller
/// passes a shared name, which is what an isolation test needs: two contexts, two principals, ONE
/// database, so the only thing separating the rows is the query filter under test.
/// </summary>
public static class DatabasesTestContext
{
    /// <summary>Creates a context over a fresh database, seen as <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal whose tenant scope the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <param name="saveFailure">When given, the exception the next save throws.</param>
    public static DatabasesDbContext Create(
        ICurrentUser currentUser,
        string? databaseName = null,
        Exception? saveFailure = null)
    {
        var builder = new DbContextOptionsBuilder<DatabasesDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        if (saveFailure is not null)
        {
            builder.AddInterceptors(new FailingSaveInterceptor(saveFailure));
        }

        return new DatabasesDbContext(builder.Options, currentUser);
    }

    /// <summary>Builds a database row for <paramref name="accountId"/> under <paramref name="username"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which forms the prefix.</param>
    /// <param name="name">The name the customer asked for.</param>
    /// <param name="dbUserName">The dedicated user's suffix; defaults to the database's own name.</param>
    public static Database Row(Guid accountId, string username, string name, string? dbUserName = null)
    {
        var userSuffix = dbUserName ?? name;

        return new Database(
            Guid.NewGuid(),
            accountId,
            name,
            $"{username}_{name}",
            $"{username}_{userSuffix}",
            userSuffix,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
