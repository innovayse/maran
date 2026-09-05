using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="SftpDbContext"/> instances for a named tenant, plus the rows to seed
/// them with. Each context gets its own uniquely-named in-memory database unless a caller passes a
/// shared name, which is what an isolation test needs: two contexts, two principals, ONE database,
/// so the only thing separating the rows is the query filter under test.
/// </summary>
public static class SftpTestContext
{
    /// <summary>Creates a context over a fresh database, seen as <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal whose tenant scope the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <param name="saveFailure">When given, the exception the next save throws.</param>
    public static SftpDbContext Create(
        ICurrentUser currentUser,
        string? databaseName = null,
        Exception? saveFailure = null)
    {
        var builder = new DbContextOptionsBuilder<SftpDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        if (saveFailure is not null)
        {
            builder.AddInterceptors(new FailingSaveInterceptor(saveFailure));
        }

        return new SftpDbContext(builder.Options, currentUser);
    }

    /// <summary>Builds an SFTP login row for <paramref name="accountId"/> under <paramref name="username"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which forms the prefix.</param>
    /// <param name="name">The name the customer asked for.</param>
    public static SftpUser Row(Guid accountId, string username, string name)
    {
        return new SftpUser(
            Guid.NewGuid(),
            accountId,
            name,
            $"{username}_{name}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
