using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Resources;
using Maran.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>Builds isolated <see cref="NotificationsDbContext"/> instances over the in-memory provider.</summary>
/// <remarks>
/// Each context gets its own uniquely-named database unless a caller passes a shared name, which is
/// what a test spanning two contexts needs — a handler writing through one and an assertion reading
/// through another, the way a request and the screen after it do.
/// </remarks>
public static class NotificationsTestContext
{
    /// <summary>Creates a context over a fresh database, or over the named one.</summary>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <returns>The context.</returns>
    public static NotificationsDbContext Create(string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        return new NotificationsDbContext(builder.Options, new PassthroughEncryptionService());
    }

    /// <summary>Builds the protection-mode name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    /// <remarks>
    /// Real resources rather than a stub, deliberately: the tests over this resolver are about whether
    /// the shipped resx triple actually names every mode, which a stub would answer for the stub.
    /// </remarks>
    public static SmtpSecurityDisplayNames SecurityNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new SmtpSecurityDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }
}
