using Wolverine;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// A bus whose publish always fails, to prove the recorder keeps its promise not to throw into the
/// operation it records when the feed cannot be reached.
/// </summary>
public sealed class ThrowingMessageBus : IMessageBus
{
    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        throw new InvalidOperationException("the bus is down");
    }

    /// <inheritdoc />
    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<TResponse> StreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> messages,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<TResponse> StreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> messages,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public IReadOnlyList<Envelope> PreviewSubscriptions(object message)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public IDestinationEndpoint EndpointFor(string endpointName)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public IDestinationEndpoint EndpointFor(Uri uri)
    {
        throw new NotSupportedException();
    }
}
