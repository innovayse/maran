using Maran.Modules.Apps.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>Builds an <see cref="AppsDbContext"/> over an isolated in-memory database.</summary>
public static class AppsTestContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="databaseName">The database to share between contexts of one test, or null for a fresh one.</param>
    /// <returns>The context.</returns>
    public static AppsDbContext Create(string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<AppsDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        return new AppsDbContext(builder.Options);
    }
}
