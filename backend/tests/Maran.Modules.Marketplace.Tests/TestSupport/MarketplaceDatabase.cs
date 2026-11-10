using Maran.Modules.Marketplace.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// Builds the module's database context over an in-memory database. Two contexts built with one name are
/// two callers over one database, which is how the gate tests put one operation in front of another.
/// </summary>
public static class MarketplaceDatabase
{
    /// <summary>Creates a context over the in-memory database of the given name.</summary>
    /// <param name="name">The database name; a context built with the same name sees the same rows.</param>
    /// <returns>The context. The caller disposes it.</returns>
    public static MarketplaceDbContext Create(string name)
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MarketplaceDbContext(options);
    }

    /// <summary>Creates a context over a database no other context shares.</summary>
    /// <returns>The context. The caller disposes it.</returns>
    public static MarketplaceDbContext CreateIsolated()
    {
        return Create(Guid.NewGuid().ToString("N"));
    }
}
