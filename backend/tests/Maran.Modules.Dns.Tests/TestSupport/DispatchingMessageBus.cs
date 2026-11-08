using Wolverine;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IMessageBus"/> double for a handler that INVOKES another operation of the same module and
/// reads its answer: it records each message invoked, in order, and answers it from the responder the test
/// supplies, which is how a test wires the real command handler behind the bus the way Wolverine would.
/// Every other member is unreachable from the code under test and says so rather than pretending.
/// </summary>
public sealed class DispatchingMessageBus : IMessageBus
{
    /// <summary>Answers a message invoked with a reply.</summary>
    private readonly Func<object, CancellationToken, Task<object>> _responder;

    /// <summary>Everything invoked on this bus with a reply, in order.</summary>
    public List<object> Invoked { get; } = [];

    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <summary>Creates a bus that answers every invocation from <paramref name="responder"/>.</summary>
    /// <param name="responder">Handles an invoked message and returns its reply.</param>
    public DispatchingMessageBus(Func<object, CancellationToken, Task<object>> responder)
    {
        _responder = responder;
    }

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

        return (T)await _responder(message, cancellation);
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
        return [new Envelope(message)];
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
