using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// Builds an <see cref="IdentityDbContext"/> that accepts the rows a test seeds and refuses to
/// remove any of them, for the tests that ask what a handler does when its deletion cannot be
/// committed.
/// </summary>
/// <remarks>
/// Separate from the ordinary factory rather than a flag on it, deliberately: a factory with a
/// "make saving fail" switch invites a test that is shaped like a production one to opt into a
/// failure it does not mention, and a reader of that test would have to check the argument to know
/// which panel it describes. Two names, two behaviours, nothing to read past.
/// </remarks>
public static class RefusingIdentityTestContext
{
    /// <summary>A throwaway base64 256-bit key. The cipher has its own tests; this one only has to work.</summary>
    private const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Creates the context.</summary>
    /// <returns>A context over its own in-memory database that refuses every removal.</returns>
    public static IdentityDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new RefusingSaveInterceptor())
            .Options;

        return new IdentityDbContext(options, new AesGcmEncryptionService(EncryptionKey));
    }
}
