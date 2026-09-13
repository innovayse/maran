using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// Builds a fresh, isolated <see cref="IdentityDbContext"/> on the EF Core InMemory provider — the
/// service under test's own dependency, not a hand-rolled repository double — so query logic is
/// exercised as written. Each call gets a uniquely named database, so tests never share state
/// (rules/testing.md "Determinism").
/// </summary>
public static class IdentityTestContext
{
    /// <summary>A throwaway base64 256-bit key. The cipher has its own tests; this one only has to work.</summary>
    private const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Creates the context.</summary>
    /// <returns>A context over its own in-memory database.</returns>
    public static IdentityDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options, new AesGcmEncryptionService(EncryptionKey));
    }

    /// <summary>Builds the action-name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    /// <remarks>
    /// Real resources rather than a stub, deliberately: the tests over this resolver are about
    /// whether the shipped resx triple actually names every action, which a stub would answer for
    /// the stub. The same arrangement the Backups module's failure-name tests use.
    /// </remarks>
    public static AuditActionDisplayNames ActionNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new AuditActionDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }
}
