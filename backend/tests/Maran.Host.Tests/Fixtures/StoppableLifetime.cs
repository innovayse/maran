using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Fixtures;

/// <summary>An <see cref="IHostApplicationLifetime"/> whose stopping token a test fires.</summary>
public sealed class StoppableLifetime : IHostApplicationLifetime, IDisposable
{
    /// <summary>The source behind <see cref="ApplicationStopping"/>.</summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <inheritdoc />
    public CancellationToken ApplicationStarted
    {
        get
        {
            return CancellationToken.None;
        }
    }

    /// <inheritdoc />
    public CancellationToken ApplicationStopping
    {
        get
        {
            return _stopping.Token;
        }
    }

    /// <inheritdoc />
    public CancellationToken ApplicationStopped
    {
        get
        {
            return CancellationToken.None;
        }
    }

    /// <summary>Signals that the application is stopping.</summary>
    public void Stop()
    {
        _stopping.Cancel();
    }

    /// <inheritdoc />
    public void StopApplication()
    {
        Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
    }
}
