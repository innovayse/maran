using Maran.Modules.Tasks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// A real container holding the one service <c>StartupTaskReconciler</c> resolves per pass.
/// </summary>
/// <remarks>
/// A real <see cref="IServiceScopeFactory"/> and not a double, because the scope is part of the
/// behaviour under test: the reconciler is a singleton hosted service and its database context is
/// scoped, so it must open a scope per pass rather than capture one. A hand-written double would
/// let a reconciler that captured a context pass.
/// </remarks>
public sealed class TestScopeFactory : IDisposable
{
    /// <summary>The container the scopes come out of.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The factory to hand the reconciler.</summary>
    public IServiceScopeFactory Scopes { get; }

    /// <summary>Builds a container serving <paramref name="dbContext"/>.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    public TestScopeFactory(TasksDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            return dbContext;
        });

        _provider = services.BuildServiceProvider();
        Scopes = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
