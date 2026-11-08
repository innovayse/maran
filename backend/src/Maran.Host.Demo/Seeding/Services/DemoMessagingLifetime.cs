using Maran.Host.Demo.Seeding.Exceptions;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Starts the message runtime for the length of a command-line run and stops it after. A command-line
/// run builds the container and starts no hosted service, and the message bus has no handlers until its
/// runtime has started. Nothing else is started: no listener, no scheduler, no port.
/// </summary>
public sealed class DemoMessagingLifetime
{
    /// <summary>The namespace root of the message library whose runtime is started.</summary>
    private const string MessagingNamespace = "Wolverine";

    /// <summary>The hosted services registered in the container.</summary>
    private readonly IEnumerable<IHostedService> _hostedServices;

    /// <summary>Creates the lifetime.</summary>
    /// <param name="hostedServices">The hosted services registered in the container.</param>
    public DemoMessagingLifetime(IEnumerable<IHostedService> hostedServices)
    {
        _hostedServices = hostedServices;
    }

    /// <summary>Runs <paramref name="work"/> with the message runtime started.</summary>
    /// <param name="work">The work that sends commands.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the work is done and the runtime stopped.</returns>
    /// <exception cref="DemoSeedException">The container holds no message runtime to start.</exception>
    public async Task RunAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        var runtimes = _hostedServices
            .Where(service =>
            {
                return service.GetType().Namespace?.StartsWith(MessagingNamespace, StringComparison.Ordinal) == true;
            })
            .ToList();
        if (runtimes.Count == 0)
        {
            throw new DemoSeedException("messaging", "MessageRuntimeNotRegistered");
        }

        foreach (var runtime in runtimes)
        {
            await runtime.StartAsync(cancellationToken);
        }

        try
        {
            await work();
        }
        finally
        {
            foreach (var runtime in runtimes.AsEnumerable().Reverse())
            {
                await runtime.StopAsync(CancellationToken.None);
            }
        }
    }
}
