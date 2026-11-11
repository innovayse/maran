using Maran.Modules.Apps.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>Builds an <see cref="AppsDbContext"/> over an isolated in-memory database.</summary>
public static class AppsTestContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="databaseName">The database to share between contexts of one test, or null for a fresh one.</param>
    /// <param name="interceptor">An interceptor to attach, or null for none.</param>
    /// <returns>The context.</returns>
    public static AppsDbContext Create(string? databaseName = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppsDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new AppsDbContext(builder.Options);
    }
}
