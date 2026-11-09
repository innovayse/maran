using Wolverine;

namespace Maran.Host.Tests.Fixtures;

/// <summary>
/// An <see cref="IMessageBus"/> double for a handler that INVOKES other queries and reads their
/// answers: it records each message invoked, in order, and answers it from a responder the test
/// supplies, as the real bus would answer from the query's own handler. Every other member is
/// unreachable from the code under test and says so rather than pretending.
/// </summary>
public sealed class ScriptedMessageBus : IMessageBus
{
    /// <summary>Everything invoked on this bus with a reply, in order.</summary>
    public List<object> Invoked { get; } = [];

    /// <summary>
    /// Answers a message invoked with a reply, given the cancellation token the invoker passed; set by the
    /// test. A responder that waits on the token is how a test makes a module hang.
    /// </summary>
    public Func<object, CancellationToken, Task<object>>? Responder { get; init; }

    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public async Task<T> InvokeAsync<T>(
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null)
    {
        Invoked.Add(message);
        if (Responder is null)
        {
            throw new NotSupportedException();
        }

        return (T)await Responder(message, cancellation);
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
