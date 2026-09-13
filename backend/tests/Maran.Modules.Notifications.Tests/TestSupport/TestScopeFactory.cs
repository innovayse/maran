using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>A real container holding the services a send resolves, so the settings cache is a real one.</summary>
/// <remarks>
/// A real <see cref="IServiceScopeFactory"/> and not a double, because the scope is part of the
/// behaviour under test: <see cref="SmtpSettingsCache"/> is a singleton and the database context it
/// reads is scoped, so it must open a scope per load rather than capture one. A hand-written double
/// would let a cache that captured a context pass.
///
/// The registration mirrors <c>NotificationsModule.ConfigureServices</c> — the cache a singleton
/// resolving its own scopes — so a load that resolves something the panel does not register fails
/// here rather than on a customer's server at midnight.
/// </remarks>
public sealed class TestScopeFactory : IDisposable
{
    /// <summary>The container the scopes come out of.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The mail-settings cache, so a test can invalidate it after seeding a row.</summary>
    public SmtpSettingsCache Settings { get; }

    /// <summary>Builds a container serving what a load and a send resolve.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    public TestScopeFactory(NotificationsDbContext dbContext)
    {
        var services = new ServiceCollection();

        // The one instance the test owns, registered rather than constructed by the container: a
        // scoped factory registration makes the container dispose the context at the end of each
        // load, so a test that reads the store afterwards gets ObjectDisposedException instead of an
        // answer.
        services.AddSingleton(dbContext);

        // A factory delegate rather than a type registration, because the cache resolves scopes from
        // the very container it lives in — which does not exist yet at registration time.
        services.AddSingleton(provider =>
        {
            return new SmtpSettingsCache(provider.GetRequiredService<IServiceScopeFactory>());
        });

        _provider = services.BuildServiceProvider();
        Settings = _provider.GetRequiredService<SmtpSettingsCache>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
