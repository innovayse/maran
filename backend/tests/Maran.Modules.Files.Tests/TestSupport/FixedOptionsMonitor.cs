using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>An options monitor that always answers one value.</summary>
/// <typeparam name="T">The options type.</typeparam>
public sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
{
    /// <summary>Creates the monitor.</summary>
    /// <param name="value">The value every read answers.</param>
    public FixedOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    /// <inheritdoc />
    public T CurrentValue { get; }

    /// <inheritdoc />
    public T Get(string? name)
    {
        return CurrentValue;
    }

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener)
    {
        return null;
    }
}
