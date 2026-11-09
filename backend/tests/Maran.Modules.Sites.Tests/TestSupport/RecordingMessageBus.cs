using Wolverine;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IMessageBus"/> double that records what the module PUBLISHED and what it INVOKED inline.
/// The retention handler publishes a message and waits for nothing, and the creation announcement invokes
/// one and waits for its subscribers, so a test needs three behaviours from the bus: keep what was
/// published, keep what was invoked (refusing it when told to), and say whether anyone subscribes. Every
/// other member is unreachable from the code under test and says so rather than pretending.
/// </summary>
public sealed class RecordingMessageBus : IMessageBus
{
    /// <summary>Everything published on this bus, in order.</summary>
    public List<object> Published { get; } = [];

    /// <summary>Everything invoked inline on this bus, in order.</summary>
    public List<object> Invoked { get; } = [];

    /// <summary>
    /// Whether any module subscribes to what is invoked; <c>false</c> models a panel composed without the
    /// module that would answer.
    /// </summary>
    public bool HasSubscribers { get; init; } = true;

    /// <summary>What every inline invocation throws, or null to accept it.</summary>
    public Exception? Refusal { get; init; }

    /// <summary>What the optional probe read at the moment of the last publish, or null when there is no probe.</summary>
    public int? RetentionSeenAtPublish { get; private set; }

    /// <summary>Reads a figure from the store at the moment of every publish, or null for no probe.</summary>
    private readonly Func<int>? _probe;

    /// <summary>Creates a bus that records what is published.</summary>
    /// <param name="probe">
    /// Read at the moment of every publish and kept in <see cref="RetentionSeenAtPublish"/>, so a test can
    /// show that a row was already saved when the message went out.
    /// </param>
    public RecordingMessageBus(Func<int>? probe = null)
    {
        _probe = probe;
    }

    /// <inheritdoc />
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        RetentionSeenAtPublish = _probe?.Invoke();

        if (message is not null)
        {
            Published.Add(message);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        Invoked.Add(message);

        return Refusal is null ? Task.CompletedTask : Task.FromException(Refusal);
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
        return HasSubscribers ? [new Envelope(message)] : [];
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
