using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

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
}
