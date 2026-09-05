using Maran.Modules.Monitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>Builds isolated <see cref="MonitoringDbContext"/> instances over the in-memory provider.</summary>
/// <remarks>
/// Each context gets its own uniquely-named database unless a caller passes a shared name, which is
/// what a test spanning two contexts needs — a handler writing through one and an assertion reading
/// through another, the way a request and the screen after it do.
/// </remarks>
public static class MonitoringTestContext
{
    /// <summary>Creates a context over a fresh database, or over the named one.</summary>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <returns>The context.</returns>
    public static MonitoringDbContext Create(string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        return new MonitoringDbContext(builder.Options);
    }
}
