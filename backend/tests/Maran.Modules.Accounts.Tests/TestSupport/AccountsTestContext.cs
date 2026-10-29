using Maran.Modules.Accounts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// Builds a fresh, isolated <see cref="AccountsDbContext"/> on the EF Core InMemory provider, each
/// call over its own uniquely named database so no two tests share state (rules/testing.md
/// "Determinism"). The plan-management tests use it instead of repeating the same four lines.
/// </summary>
public static class AccountsTestContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="interceptors">Interceptors to attach, for a test that arranges a failed save.</param>
    /// <returns>A context over its own in-memory database.</returns>
    public static AccountsDbContext Create(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options;

        return new AccountsDbContext(options);
    }
}
