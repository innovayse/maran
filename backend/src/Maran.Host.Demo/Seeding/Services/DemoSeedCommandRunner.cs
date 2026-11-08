using Maran.Host.Demo.Seeding.Exceptions;
using Maran.SharedKernel.Results;
using Wolverine;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Sends a command through the same message bus, handlers and validators an HTTP request reaches, and
/// turns a failed result into a <see cref="DemoSeedException"/>. This is what makes every seeded row
/// come from a real handler and not from a second description of the data.
/// </summary>
public sealed class DemoSeedCommandRunner
{
    /// <summary>The origin recorded in the journal for every seeded change: this host, not a visitor.</summary>
    public const string Origin = "127.0.0.1";

    /// <summary>The agent string recorded in the journal for every seeded change.</summary>
    public const string Agent = "maran-demo-seed";

    /// <summary>The bus commands go through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the runner.</summary>
    /// <param name="bus">The bus commands go through.</param>
    public DemoSeedCommandRunner(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <summary>Sends a command whose handler answers with a value.</summary>
    /// <typeparam name="TValue">The value the handler answers with.</typeparam>
    /// <param name="step">The step, named in a failure.</param>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>The handler's value.</returns>
    /// <exception cref="DemoSeedException">The handler refused.</exception>
    public async Task<TValue> RunAsync<TValue>(string step, object command, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<TValue>>(command, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new DemoSeedException(step, result.Error!.Code);
        }

        return result.Value;
    }

    /// <summary>Sends a command whose handler answers with success or failure only.</summary>
    /// <param name="step">The step, named in a failure.</param>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the handler succeeded.</returns>
    /// <exception cref="DemoSeedException">The handler refused.</exception>
    public async Task RunAsync(string step, object command, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result>(command, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new DemoSeedException(step, result.Error!.Code);
        }
    }
}
