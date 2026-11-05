using Microsoft.EntityFrameworkCore;
using Maran.Modules.Idempotency.Persistence;

namespace Maran.Modules.Idempotency.Tests.TestSupport;

/// <summary>Opens an <see cref="IdempotencyDbContext"/> over a private in-memory database.</summary>
/// <remarks>
/// Each name is its own database, so two contexts opened with one name share rows, which is how a test
/// plays two requests that arrive on two scopes of the same panel.
/// </remarks>
public static class InMemoryIdempotencyDatabase
{
    /// <summary>A context over the database <paramref name="name"/>.</summary>
    /// <param name="name">The database's name; a fresh one per test unless the test shares it.</param>
    /// <returns>The context.</returns>
    public static IdempotencyDbContext Open(string name)
    {
        var options = new DbContextOptionsBuilder<IdempotencyDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new IdempotencyDbContext(options);
    }
}
