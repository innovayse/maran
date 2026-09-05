using Maran.Agent.Client.Interfaces;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Resources;
using Maran.Modules.Monitoring.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>A real container holding just the services one sampling round resolves.</summary>
/// <remarks>
/// A real <see cref="IServiceScopeFactory"/> and not a double, because the scope is part of the
/// behaviour under test: the sampler is a singleton hosted service and its database context is
/// scoped, so it must open a scope per round rather than capture one. A hand-written double would
/// let a sampler that captured a context pass.
///
/// Registrations mirror <c>MonitoringModule.ConfigureServices</c> — the journal and the evaluator
/// scoped — so a round that resolves something the panel does not register fails here rather than on
/// a customer's server at midnight. Nothing about mail is registered: the evaluator publishes a
/// request and the Notifications module sends it, so what this container serves for mail is a
/// recording bus and a stub address.
/// </remarks>
public sealed class TestScopeFactory : IDisposable
{
    /// <summary>The container the scopes come out of.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The factory to hand the sampler.</summary>
    public IServiceScopeFactory Scopes { get; }

    /// <summary>Everything the round published, so a test can see the mail it asked for.</summary>
    public RecordingMessageBus Bus { get; }

    /// <summary>Builds a container serving everything one sampling round resolves.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    /// <param name="agent">The agent client every scope resolves.</param>
    /// <param name="recipients">The stub answering where an operator alert is addressed.</param>
    /// <param name="audit">The journal double a round writes its decisions to.</param>
    public TestScopeFactory(
        MonitoringDbContext dbContext,
        IAgentMonitorClient agent,
        IAlertRecipientDirectory recipients,
        IAuditWriter audit)
    {
        var services = new ServiceCollection();

        // The one instance the test owns, registered rather than constructed by the container: a
        // scoped factory registration makes the container dispose the context at the END OF EACH
        // ROUND, so a test that reads the store afterwards — to see what the round wrote — gets
        // ObjectDisposedException instead of an answer.
        services.AddSingleton(dbContext);
        services.AddSingleton(agent);
        services.AddSingleton(recipients);
        services.AddSingleton(audit);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser());
        services.AddSingleton<IStringLocalizer<NotificationMessages>>(new StubStringLocalizer<NotificationMessages>());

        Bus = new RecordingMessageBus();
        services.AddSingleton<IMessageBus>(Bus);

        services.AddScoped<MonitoringAuditJournal>();
        services.AddScoped<AlertEvaluator>();

        _provider = services.BuildServiceProvider();
        Scopes = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>Resolves one of the round's scoped services, for a test that drives it directly.</summary>
    /// <typeparam name="T">The service to resolve.</typeparam>
    /// <returns>The instance, from a scope the container owns for the life of this factory.</returns>
    public T Resolve<T>()
        where T : notnull
    {
        return _provider.CreateScope().ServiceProvider.GetRequiredService<T>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
