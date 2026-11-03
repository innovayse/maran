using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Interfaces;
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
    /// <summary>
    /// Creates the context for an administrator, which the reseller filters do not narrow.
    /// </summary>
    /// <param name="interceptors">Interceptors to attach, for a test that arranges a failed save.</param>
    /// <returns>A context over its own in-memory database.</returns>
    public static AccountsDbContext Create(params IInterceptor[] interceptors)
    {
        return Create(FakeCurrentUser.Admin(), interceptors);
    }

    /// <summary>Creates the context for a given caller, whose reseller scope the filters close over.</summary>
    /// <param name="currentUser">The principal the context's query filters are evaluated for.</param>
    /// <param name="interceptors">Interceptors to attach, for a test that arranges a failed save.</param>
    /// <returns>A context over its own in-memory database.</returns>
    public static AccountsDbContext Create(ICurrentUser currentUser, params IInterceptor[] interceptors)
    {
        return CreateOn(Guid.NewGuid().ToString(), currentUser, interceptors);
    }

    /// <summary>
    /// Creates a context over the named in-memory database, so a test can seed as one principal and
    /// read the very same rows as another by naming the database twice.
    /// </summary>
    /// <param name="databaseName">The in-memory database to open; the same name is the same rows.</param>
    /// <param name="currentUser">The principal the context's query filters are evaluated for.</param>
    /// <param name="interceptors">Interceptors to attach, for a test that arranges a failed save.</param>
    /// <returns>A context over the named database, filtered for <paramref name="currentUser"/>.</returns>
    public static AccountsDbContext CreateOn(
        string databaseName,
        ICurrentUser currentUser,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .Options;

        return new AccountsDbContext(options, currentUser);
    }
}
