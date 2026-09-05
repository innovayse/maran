using Wolverine;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>
/// An <see cref="IMessageBus"/> double that records what the module PUBLISHED. The alert evaluator
/// asks for a mail and waits for nothing, so a test needs exactly one behaviour from the bus: keep
/// what was published, in order. Every other member is unreachable from the code under test and says
/// so rather than pretending.
/// </summary>
public sealed class RecordingMessageBus : IMessageBus
{
    /// <summary>Everything published on this bus, in order.</summary>
    public List<object> Published { get; } = [];

    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
        {
            Published.Add(message);
        }

        return ValueTask.CompletedTask;
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
