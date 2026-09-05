using Maran.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// A scope factory over one context — like <see cref="SingleContextScopeFactory"/>, which is sealed
/// and so cannot be extended — that runs one action, once, when the scope it handed out is disposed.
/// </summary>
/// <remarks>
/// This is how a test reaches the one instant that matters to <c>SecurityPolicyCache</c> without a
/// sleep and without racing for it (rules/testing.md "Determinism"). The cache loads inside a scope
/// and publishes the result after that scope is disposed, so an action run on disposal runs after
/// the policy row has been read and before the load's value reaches the field — which is where a
/// concurrent save has to land for the cache to lose it.
/// </remarks>
public sealed class CallbackScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    /// <summary>The context every scope resolves.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>What to run when the first scope is disposed, or <c>null</c> once it has run.</summary>
    private Action? _onScopeDisposed;

    /// <summary>Binds the factory to its context and its one-shot action.</summary>
    /// <param name="dbContext">The context to resolve.</param>
    /// <param name="onScopeDisposed">Runs when the first scope handed out is disposed.</param>
    public CallbackScopeFactory(IdentityDbContext dbContext, Action onScopeDisposed)
    {
        _dbContext = dbContext;
        _onScopeDisposed = onScopeDisposed;
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

    /// <summary>Runs the action the first time a scope is disposed, and never again.</summary>
    public void Dispose()
    {
        var action = _onScopeDisposed;
        _onScopeDisposed = null;
        action?.Invoke();
    }
}
