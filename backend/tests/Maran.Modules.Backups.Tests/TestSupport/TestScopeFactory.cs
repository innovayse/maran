using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// A real container holding just the services <see cref="StartupBackupReconciler"/> resolves per pass.
/// </summary>
/// <remarks>
/// A real <see cref="IServiceScopeFactory"/> and not a double, because opening a scope and resolving
/// from it is part of the behaviour under test: the reconciler is a singleton hosted service whose
/// dependencies are scoped, so a pass that resolves something the panel does not register fails here
/// rather than at a customer's first reboot. What this cannot catch is a reconciler that CAPTURED a
/// scoped dependency instead of resolving one — the panel's own container refuses that at build time,
/// which is where the reconciler's own remarks say the protection lives.
/// </remarks>
public sealed class TestScopeFactory : IDisposable
{
    /// <summary>The container the scopes come out of.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The factory to hand the reconciler.</summary>
    public IServiceScopeFactory Scopes { get; }

    /// <summary>Builds a container serving everything one reclamation pass resolves.</summary>
    /// <param name="dbContext">The context every scope resolves.</param>
    /// <param name="audit">The journal double a pass writes its decisions to.</param>
    /// <param name="currentUser">The principal the context's tenant filter closes over.</param>
    /// <param name="accounts">The account directory a pass resolves the owning user name from.</param>
    public TestScopeFactory(
        BackupsDbContext dbContext,
        IAuditWriter audit,
        ICurrentUser currentUser,
        IAccountDirectory accounts)
    {
        var services = new ServiceCollection();

        // The one instance the test owns, registered rather than constructed by the container: a
        // scoped factory registration makes the container dispose the context at the END OF EACH
        // PASS, so a test that reads the store afterwards — to see what the pass wrote — gets
        // ObjectDisposedException instead of an answer.
        services.AddSingleton(dbContext);
        services.AddSingleton(audit);
        services.AddSingleton(currentUser);

        // Registered SCOPED, exactly as the Accounts module registers the real one — which is the
        // whole reason the reconciler resolves it per pass instead of taking it in its constructor.
        // A singleton registration here would let a captured dependency pass this fixture and fail
        // the panel's own container at build time.
        services.AddScoped(_ => { return accounts; });

        // Registered exactly as BackupsModule registers them, so a pass that resolves something the
        // panel does not register fails here rather than at a customer's first reboot.
        services.AddScoped<BackupDestinationResolver>();
        services.AddScoped<BackupAuditJournal>();

        _provider = services.BuildServiceProvider();
        Scopes = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
