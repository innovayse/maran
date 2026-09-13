using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// A real container holding just the services <c>StartupBanReconciler</c> resolves per pass.
/// </summary>
/// <remarks>
/// A real <see cref="IServiceScopeFactory"/> and not a double, because opening a scope and resolving
/// from it is part of the behaviour under test: the reconciler is a singleton hosted service whose
/// dependencies are scoped, so a pass that resolved something the panel does not register fails here
/// rather than at a customer's first reboot. What this cannot catch is a reconciler that CAPTURED a
/// scoped dependency instead of resolving one — the panel's own container refuses that at build
/// time, which is where <see cref="Maran.Modules.Firewall.Services.StartupBanReconciler"/> says the
/// protection lives.
/// </remarks>
public sealed class TestScopeFactory : IDisposable
{
    /// <summary>The container the scopes come out of.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The factory to hand the reconciler.</summary>
    public IServiceScopeFactory Scopes { get; }

    /// <summary>Builds a container serving everything one reconciliation pass resolves.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    /// <param name="agent">The agent client every scope resolves.</param>
    /// <param name="audit">The journal double a pass writes its decisions to.</param>
    public TestScopeFactory(FirewallDbContext dbContext, IAgentFirewallClient agent, IAuditWriter audit)
    {
        var services = new ServiceCollection();

        // The one instance the test owns, registered rather than constructed by the container: a
        // scoped factory registration makes the container dispose the context at the END OF EACH
        // PASS, so a test that reads the store afterwards — to see what the pass wrote — gets
        // ObjectDisposedException instead of an answer.
        services.AddSingleton(dbContext);
        services.AddSingleton(agent);
        services.AddSingleton(audit);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser());

        // Registered exactly as FirewallModule registers them, so a pass that resolves something the
        // panel does not register fails here rather than at a customer's first reboot.
        services.AddScoped<WhitelistGuard>();
        services.AddScoped<FirewallAuditJournal>();

        _provider = services.BuildServiceProvider();
        Scopes = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
