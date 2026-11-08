using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// A real container holding just the services <c>DnsZoneReconciler</c> resolves per pass.
/// </summary>
/// <remarks>
/// A real <see cref="IServiceScopeFactory"/> and not a double, because opening a scope and resolving from it
/// is part of the behaviour under test: the reconciler is a singleton whose dependencies are scoped.
/// </remarks>
public sealed class DnsReconcilerScopeFactory : IDisposable
{
    /// <summary>The container the scopes come out of.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The factory to hand the reconciler.</summary>
    public IServiceScopeFactory Scopes { get; }

    /// <summary>Builds a container serving everything one pass resolves.</summary>
    /// <param name="dbContext">The context every scope resolves; the start-up caller's, which sees no tenant's rows.</param>
    /// <param name="agent">The agent client every scope resolves.</param>
    /// <param name="zoneLock">The lock every scope resolves; a lock that does nothing when omitted.</param>
    public DnsReconcilerScopeFactory(DnsDbContext dbContext, IAgentDnsClient agent, IDnsZoneLock? zoneLock = null)
    {
        var services = new ServiceCollection();

        // Registered instances, so the container does not dispose the context at the end of each pass and a
        // test can still read the store afterwards.
        services.AddSingleton(dbContext);
        services.AddSingleton(agent);
        services.AddSingleton(zoneLock ?? new LocklessDnsZoneLock(dbContext));
        services.AddSingleton<IClock>(new FixedClock());

        _provider = services.BuildServiceProvider();
        Scopes = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
