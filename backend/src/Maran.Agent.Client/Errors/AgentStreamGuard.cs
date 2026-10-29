using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Maran.Agent.Client.Errors;

/// <summary>
/// Gives a server-streaming agent call the same ending a unary one gets when the TRANSPORT fails: a
/// typed terminal event instead of the raw <see cref="RpcException"/> the generated client throws.
/// </summary>
/// <remarks>
/// <para>
/// A stream's contract is that it always ends in a stated way. The agent can state an ending in the
/// stream itself, and the clients map every such ending to a terminal event; but an agent that cannot
/// be reached, or a connection torn mid-stream, states nothing, and the exception the transport raises
/// used to travel up through the caller to the last-resort handler as an anonymous 500 (issue #87).
/// This guard sits on the OUTPUT of a client's own iterator — the events, not the wire messages — and
/// turns that exception into one final event the caller's existing terminal handling already knows how
/// to read.
/// </para>
/// <para>
/// A stream read against an agent whose socket cannot be connected to does not always raise a status:
/// <see cref="Channels.AgentChannel"/> sets <c>ThrowOperationCanceledOnCancellation</c>, and the
/// stream reader then reports the connect failure as an <see cref="OperationCanceledException"/>
/// although nobody cancelled (measured against <c>Grpc.Net.Client</c> 2.84.0 on a socket path that
/// does not exist, by <c>AgentStreamGuardTests</c>). A cancellation the caller's token does not
/// account for is the transport giving up, so it ends the stream the same way a
/// <see cref="StatusCode.Cancelled"/> status does, and takes the same typed answer.
/// </para>
/// <para>
/// What it never swallows: a cancellation the caller asked for (the caller leaving is not a failure
/// of the agent) and any exception that is neither a transport status nor such a cancellation, which
/// is a defect of the panel and must stay loud.
/// </para>
/// </remarks>
public static class AgentStreamGuard
{
    /// <summary>
    /// Forwards <paramref name="source"/> unchanged until the transport fails, then yields the one
    /// terminal event <paramref name="toTerminal"/> builds and ends.
    /// </summary>
    /// <typeparam name="TEvent">The client's event type.</typeparam>
    /// <param name="source">The client's own event sequence, which may throw a transport status.</param>
    /// <param name="toTerminal">Builds the failed terminal event from the status the transport raised.</param>
    /// <param name="cancellationToken">The caller's token; a status raised after it fired is not mapped.</param>
    /// <returns>The events of <paramref name="source"/>, followed by the terminal event when it failed.</returns>
    public static async IAsyncEnumerable<TEvent> EndOnTransportFailureAsync<TEvent>(
        IAsyncEnumerable<TEvent> source,
        Func<RpcException, TEvent> toTerminal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(toTerminal);

        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var current = default(TEvent)!;
            var failed = false;

            // A yield cannot sit inside a try block that has a catch, so the failure is recorded here
            // and yielded below.
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    yield break;
                }

                current = enumerator.Current;
            }
            catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
            {
                current = toTerminal(exception);
                failed = true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                current = toTerminal(new RpcException(new Status(StatusCode.Cancelled, string.Empty)));
                failed = true;
            }

            yield return current;

            if (failed)
            {
                yield break;
            }
        }
    }
}
