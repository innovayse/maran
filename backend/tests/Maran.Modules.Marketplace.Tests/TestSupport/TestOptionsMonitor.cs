using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>An options monitor that returns one instance a test may change between calls.</summary>
/// <typeparam name="T">The options type.</typeparam>
public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    /// <summary>The instance returned for every name.</summary>
    private readonly T _value;

    /// <summary>Creates the monitor.</summary>
    /// <param name="value">The instance to return.</param>
    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    /// <inheritdoc />
    public T CurrentValue
    {
        get
        {
            return _value;
        }
    }

    /// <inheritdoc />
    public T Get(string? name)
    {
        return _value;
    }

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener)
    {
        return null;
    }
}
