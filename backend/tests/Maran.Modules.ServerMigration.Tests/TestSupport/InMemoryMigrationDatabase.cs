using Maran.Modules.ServerMigration.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>Opens a <see cref="ServerMigrationDbContext"/> over a private in-memory database.</summary>
/// <remarks>
/// Each name is its own database, so two contexts opened with one name share rows. The in-memory provider applies
/// value converters, so the cipher is in play; the unique indexes are PostgreSQL facts and are read from the model.
/// </remarks>
public static class InMemoryMigrationDatabase
{
    /// <summary>A context over the database <paramref name="name"/>, using a cipher that marks its output.</summary>
    /// <param name="name">The database's name; a fresh one per test unless the test shares it.</param>
    /// <returns>The context.</returns>
    public static ServerMigrationDbContext Open(string name)
    {
        var options = new DbContextOptionsBuilder<ServerMigrationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new ServerMigrationDbContext(options, new PrefixEncryptionService());
    }
}
