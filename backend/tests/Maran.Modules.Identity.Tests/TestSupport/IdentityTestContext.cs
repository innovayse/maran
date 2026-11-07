using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    /// <remarks>
    /// The in-memory provider throws on <c>BeginTransaction</c> unless told to ignore it, and the handlers
    /// that erase journal rows open one so that the erasure and the removal commit together. The provider
    /// keeps no transaction, so a unit test over this context cannot observe a rollback.
    /// </remarks>
    public static IdentityDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => { warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning); })
            .Options;

        return new IdentityDbContext(options, new AesGcmEncryptionService(EncryptionKey));
    }

    /// <summary>
    /// Creates a context over a named in-memory database, so a second context over the same name sees
    /// the rows the first stored — how a test plays a concurrent writer.
    /// </summary>
    /// <param name="databaseName">The in-memory database to open.</param>
    /// <param name="interceptors">Interceptors to attach to this context only.</param>
    /// <returns>A context over the named database.</returns>
    public static IdentityDbContext CreateOver(string databaseName, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .ConfigureWarnings(warnings => { warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning); })
            .Options;

        return new IdentityDbContext(options, new AesGcmEncryptionService(EncryptionKey));
    }

    /// <summary>Builds the localizer over this module's REAL display-name resource files.</summary>
    /// <returns>The localizer, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    public static IStringLocalizer<DisplayNames> DisplayNameResources()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new StringLocalizer<DisplayNames>(factory);
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

    /// <summary>Builds the audit-outcome name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    public static AuditOutcomeDisplayNames OutcomeNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new AuditOutcomeDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }
}
