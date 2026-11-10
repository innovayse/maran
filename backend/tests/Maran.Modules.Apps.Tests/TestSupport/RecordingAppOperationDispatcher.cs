using Maran.Modules.Apps.Interfaces;
using Maran.Modules.Apps.Jobs;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>An <see cref="IAppOperationDispatcher"/> double that keeps what it was handed and can be told to refuse.</summary>
public sealed class RecordingAppOperationDispatcher : IAppOperationDispatcher
{
    /// <summary>Every operation handed over, in order.</summary>
    public List<AppOperationRequested> Dispatched { get; } = [];

    /// <summary>Whether the dispatch throws, as a bus that is down would.</summary>
    public bool Throws { get; set; }

    /// <inheritdoc />
    public Task DispatchAsync(AppOperationRequested request, CancellationToken ct)
    {
        if (Throws)
        {
            throw new InvalidOperationException("The bus is down.");
        }

        Dispatched.Add(request);
        return Task.CompletedTask;
    }
}
