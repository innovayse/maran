namespace Maran.Host.DemoIntegrationTests.Fixtures;

/// <summary>One seeded demo host shared by the tests of a class that change nothing in it.</summary>
public sealed class SharedSeededDemo : IAsyncLifetime
{
    /// <summary>The shared server.</summary>
    private readonly DemoPostgres _postgres;

    /// <summary>The host, once <see cref="InitializeAsync"/> has run.</summary>
    private SeededDemo? _demo;

    /// <summary>The administrator's client, signed in once: the login limit would refuse a sign-in per test.</summary>
    private HttpClient? _client;

    /// <summary>The administrator's signed-in client; available to the tests, after initialization.</summary>
    public HttpClient Client
    {
        get { return _client ?? throw new InvalidOperationException("The demo host has not started."); }
    }

    /// <summary>The host; available to the tests, after initialization.</summary>
    public SeededDemo Demo
    {
        get { return _demo ?? throw new InvalidOperationException("The demo host has not started."); }
    }

    /// <summary>Binds to the shared server.</summary>
    /// <param name="postgres">The shared server.</param>
    public SharedSeededDemo(DemoPostgres postgres)
    {
        _postgres = postgres;
    }

    /// <summary>Boots, migrates and seeds the host.</summary>
    /// <returns>Resolves once it is ready.</returns>
    public async Task InitializeAsync()
    {
        _demo = await SeededDemo.StartAsync(_postgres);
        _client = await _demo.SignInAsync();
    }

    /// <summary>Stops the host.</summary>
    /// <returns>Resolves once it is gone.</returns>
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_demo is not null)
        {
            await _demo.DisposeAsync();
        }
    }
}
