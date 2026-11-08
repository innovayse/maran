using Maran.Host.Demo.Seeding.Exceptions;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Fills the guard's protected set when the API starts, because the seed that created the resources ran
/// in another process and its in-memory set did not travel. An API that finds nothing to protect refuses
/// to start: a served demo that guards no identity is the one state the guard exists to prevent.
/// </summary>
public sealed class DemoProtectedResourceStartup : IHostedService
{
    /// <summary>Reads the resources and marks them.</summary>
    private readonly DemoProtectedResourceLoader _loader;

    /// <summary>Logged once at start-up with how many identity-bearing resources the guard protects.</summary>
    private static readonly Action<ILogger, int, Exception?> LogProtected =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(DemoProtectedResourceStartup)),
            "Demo guard protects {Count} identity-bearing resources.");

    /// <summary>Says how many were marked.</summary>
    private readonly ILogger<DemoProtectedResourceStartup> _logger;

    /// <summary>Creates the start-up step.</summary>
    /// <param name="loader">Reads the resources and marks them.</param>
    /// <param name="logger">Says how many were marked.</param>
    public DemoProtectedResourceStartup(
        DemoProtectedResourceLoader loader,
        ILogger<DemoProtectedResourceStartup> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var count = await _loader.LoadAsync(cancellationToken);
        if (count == 0)
        {
            throw new DemoSeedException("protection", "NoResourceProtected");
        }

        LogProtected(_logger, count, null);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
