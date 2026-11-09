using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World;

namespace Maran.Host.Demo.Seeding;

/// <summary>
/// Builds the demonstration in an empty, migrated database by running every <see cref="IDemoSeedStep"/>
/// in registration order. Every row comes from the real command handlers; the steps that need a state
/// no command produces say so in their own remarks.
/// </summary>
/// <remarks>
/// Once the administrator exists the seed acts as that administrator, so the run is journalled and
/// scoped exactly as an administrator's own requests are. Running it twice fails at the
/// administrator step, because first-run setup closes the moment a user exists.
/// </remarks>
public sealed class DemoSeeder
{
    /// <summary>Opens the scope the steps are resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Switches the caller the seed acts as.</summary>
    private readonly DemoSeedCallerAccessor _caller;

    /// <summary>Marks the identity-bearing resources the guard must refuse to remove.</summary>
    private readonly DemoProtectedResourceLoader _protection;

    /// <summary>Creates the seeder.</summary>
    /// <param name="scopeFactory">Opens the scope the steps are resolved in.</param>
    /// <param name="caller">Switches the caller the seed acts as.</param>
    /// <param name="protection">Marks the identity-bearing resources the guard must refuse to remove.</param>
    public DemoSeeder(
        IServiceScopeFactory scopeFactory,
        DemoSeedCallerAccessor caller,
        DemoProtectedResourceLoader protection)
    {
        _scopeFactory = scopeFactory;
        _caller = caller;
        _protection = protection;
    }

    /// <summary>Seeds the world.</summary>
    /// <param name="progress">Where each step's name is written as it starts.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the last step is done.</returns>
    public async Task SeedAsync(TextWriter progress, CancellationToken cancellationToken)
    {
        var context = new DemoSeedContext(StandardDemoWorld.Instance);

        await using var scope = _scopeFactory.CreateAsyncScope();
        foreach (var step in scope.ServiceProvider.GetServices<IDemoSeedStep>())
        {
            using var acting = context.Administrator is { } administrator ? _caller.Act(administrator) : null;

            await progress.WriteLineAsync($"seeding {step.Name}");
            await step.RunAsync(context, cancellationToken);
        }

        // An empty protected set silently allows everything it guards, so a seed that marked nothing is
        // a failed seed and not a quiet one.
        await progress.WriteLineAsync("protecting identity-bearing resources");
        if (await _protection.LoadAsync(cancellationToken) == 0)
        {
            throw new DemoSeedException("protection", "NoResourceProtected");
        }
    }
}
