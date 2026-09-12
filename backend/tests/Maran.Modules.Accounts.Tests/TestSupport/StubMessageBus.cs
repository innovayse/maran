using Wolverine;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IMessageBus"/> double for the deletion cascade. The delete handler INVOKES
/// <c>AccountDeleting</c> inline and treats any exception from a subscriber as "do not delete this
/// account", so a test needs three behaviours from the bus: record what was invoked, throw, or ACT on
/// the message the way a real subscriber would. Every other member is unreachable from the handler
/// and says so rather than pretending.
/// </summary>
/// <remarks>
/// The third behaviour exists because the suspension cascade now carries a report the subscribers
/// write into (<c>AccountSuspending.Report</c>), and the default — a bus that records and does nothing
/// — is the honest double for a panel where no module that ends sessions was composed. A test about
/// what the attestation says when a subscriber DID answer has to be able to make one answer.
/// </remarks>
public sealed class StubMessageBus : IMessageBus
{
    /// <summary>What every invocation throws, or null to accept it.</summary>
    private readonly Exception? _refusal;

    /// <summary>The one subscriber this bus runs, or null when it has none.</summary>
    private readonly Action<object>? _subscriber;

    /// <summary>Creates a bus whose subscribers all accept.</summary>
    public StubMessageBus()
    {
    }

    /// <summary>Creates a bus whose subscribers refuse with <paramref name="refusal"/>.</summary>
    /// <param name="refusal">The exception every invocation throws.</param>
    public StubMessageBus(Exception refusal)
    {
        _refusal = refusal;
    }

    /// <summary>Creates a bus with one accepting subscriber that acts on every message.</summary>
    /// <param name="subscriber">
    /// Run inline before the invocation returns, exactly as Wolverine runs this cascade's handlers, so
    /// anything it writes onto the message is there when the publisher reads it.
    /// </param>
    public StubMessageBus(Action<object> subscriber)
    {
        _subscriber = subscriber;
    }


    /// <summary>Everything invoked on this bus, in order.</summary>
    public List<object> Invoked { get; } = [];

    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        Invoked.Add(message);
        if (_refusal is not null)
        {
            return Task.FromException(_refusal);
        }

        _subscriber?.Invoke(message);

        return Task.CompletedTask;
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
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
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
