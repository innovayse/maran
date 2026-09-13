using Maran.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IServiceScopeFactory"/> whose every scope resolves the one
/// <see cref="IdentityDbContext"/> it was given.
/// </summary>
/// <remarks>
/// <c>SecurityPolicyCache</c> is a singleton that opens a scope per load, because a singleton may
/// not capture a scoped context. A test wanting to exercise it against an in-memory database needs
/// something to hand that context back, and the alternative — building a whole service provider per
/// test — hides the thing under test behind a container's registration rules.
/// </remarks>
public sealed class SingleContextScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    private readonly IdentityDbContext _dbContext;

    /// <summary>Binds the factory to the context every scope will hand out.</summary>
    /// <param name="dbContext">The context to resolve.</param>
    public SingleContextScopeFactory(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>The provider this scope resolves from, which is this object.</summary>
    public IServiceProvider ServiceProvider
    {
        get
        {
            return this;
        }
    }

    /// <summary>Returns a scope, which is this object.</summary>
    /// <returns>This instance.</returns>
    public IServiceScope CreateScope()
    {
        return this;
    }

    /// <summary>Resolves a service.</summary>
    /// <param name="serviceType">The service asked for.</param>
    /// <returns>The context when it is asked for, otherwise null.</returns>
    public object? GetService(Type serviceType)
    {
        return serviceType == typeof(IdentityDbContext) ? _dbContext : null;
    }

    /// <summary>Does nothing: the context outlives the scope and belongs to the test.</summary>
    public void Dispose()
    {
    }
}
