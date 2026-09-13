using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Resources;
using Maran.Modules.Monitoring.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    /// <summary>Builds the service-name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    /// <remarks>
    /// A stub localizer would prove only that a handler calls something; the thing worth checking is
    /// that every service a row can actually carry HAS an entry under this key scheme, and that is
    /// only observable against the real resource files (rules/testing.md "A check must be able to
    /// observe what it reports on"). Same arrangement as the Backups module's name-resolver tests.
    /// </remarks>
    public static ServiceDisplayNames ServiceNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new ServiceDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }
}
