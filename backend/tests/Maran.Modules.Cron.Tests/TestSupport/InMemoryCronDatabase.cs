using Maran.Modules.Cron.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>Opens a <see cref="CronDbContext"/> over a private in-memory database.</summary>
/// <remarks>
/// Each name is its own database, so two contexts opened with one name share rows — which is how a test
/// seeds through one caller and reads through another to prove the tenant filter.
/// </remarks>
public static class InMemoryCronDatabase
{
    /// <summary>A context over the database <paramref name="name"/>, filtered for <paramref name="caller"/>.</summary>
    /// <param name="name">The database's name; a fresh one per test unless the test shares it.</param>
    /// <param name="caller">The caller the tenant filter closes over.</param>
    /// <returns>The context.</returns>
    public static CronDbContext Open(string name, ICurrentUser caller)
    {
        var options = new DbContextOptionsBuilder<CronDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new CronDbContext(options, caller);
    }
}
