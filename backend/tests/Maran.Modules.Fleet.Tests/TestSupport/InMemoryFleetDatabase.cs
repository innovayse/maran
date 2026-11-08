using Maran.Modules.Fleet.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Fleet.Tests.TestSupport;

/// <summary>Opens a <see cref="FleetDbContext"/> over a private in-memory database.</summary>
/// <remarks>
/// Each name is its own database, so two contexts opened with one name share rows, which is how a test
/// plays two panel starts that share a database.
/// </remarks>
public static class InMemoryFleetDatabase
{
    /// <summary>A context over the database <paramref name="name"/>.</summary>
    /// <param name="name">The database's name; a fresh one per test unless the test shares it.</param>
    /// <returns>The context.</returns>
    public static FleetDbContext Open(string name)
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new FleetDbContext(options);
    }
}
