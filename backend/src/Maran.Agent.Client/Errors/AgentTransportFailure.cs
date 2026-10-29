using Grpc.Core;
using Maran.Agent.Client.Resources;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Errors;

/// <summary>
/// The single place a failure of the TRANSPORT to the agent — as opposed to an answer the agent sent
/// (<see cref="AgentErrorTranslator"/>) — is classified: whether a call could not be made at all,
/// whether the agent could not be reached, and which typed <see cref="Error"/> the panel answers with.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the transport really throws.</b> <c>Grpc.Net.Client</c> never lets a
/// <see cref="System.Net.Sockets.SocketException"/> out. A socket path that does not exist and a
/// socket file nobody listens on both arrive as an <see cref="RpcException"/> with
/// <see cref="StatusCode.Unavailable"/>, carrying an <see cref="HttpRequestException"/> that carries
/// the <see cref="System.Net.Sockets.SocketException"/> one level further down (measured against
/// <c>Grpc.Net.Client</c> 2.84.0, issue #87). Every predicate here is written against that shape and
/// nothing else. The pipelines used to retry on a bare <see cref="System.Net.Sockets.SocketException"/>,
/// which is what the test fakes threw and what production never raises: the retry they guarded never
/// fired, and the outage it should have answered with a 503 reached the last-resort handler as an
/// anonymous 500.
/// </para>
/// <para>
/// <b>Who uses it.</b> The classification lives here, beside the generated client, and the policy
/// lives in the Host: <c>AgentOperationPipeline</c> retries on <see cref="IsConnectFailure"/> only and
/// answers what is left with <see cref="ToError"/>; the probe pipeline, <c>AgentCallPipeline</c>,
/// retries on the wider <see cref="IsUnreachable"/>, because the handshake it carries changes nothing.
/// </para>
/// <para>
/// The agent reports its own failures in the response payload (rules/proto.md "Response shape"), so
/// a non-OK gRPC status is never a domain answer. It is the transport, the peer-credential guard, an
/// rpc this agent build does not serve, or the server framework failing a call it could not complete —
/// and the panel says which of those it was rather than leaving the exception to the anonymous 500.
/// </para>
/// </remarks>
public static class AgentTransportFailure
{
    /// <summary>
    /// Whether <paramref name="exception"/> is a call that never reached the agent: the connection to
    /// its socket could not be established, so no request was delivered and nothing ran.
    /// </summary>
    /// <param name="exception">What the call threw.</param>
    /// <returns><c>true</c> only for a failed connect; never for a failure after the request was sent.</returns>
    /// <remarks>
    /// The test is the framework's own classification, <see cref="HttpRequestError.ConnectionError"/>,
    /// on the <see cref="HttpRequestException"/> directly beneath the <see cref="RpcException"/>.
    /// <c>SocketsHttpHandler</c> assigns that value when establishing the connection fails — here, the
    /// unix-socket <c>ConnectCallback</c> in <see cref="Channels.AgentChannel"/>, whose failure it wraps
    /// with the <c>(localhost:80)</c> suffix the issue's measurement shows — and a request is written
    /// only to a connection that was established. A socket error somewhere in the chain is NOT the test:
    /// a connection reset while the response is awaited carries one too, after the request has reached
    /// the agent, and a retry on that path would start a second copy of an operation beside the first.
    /// </remarks>
    public static bool IsConnectFailure(Exception exception)
    {
        return exception is RpcException { StatusCode: StatusCode.Unavailable } rpc
            && rpc.InnerException is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError };
    }

    /// <summary>
    /// Whether <paramref name="exception"/> says the agent could not be reached or did not answer in
    /// time, whatever the phase of the call: <see cref="StatusCode.Unavailable"/> or
    /// <see cref="StatusCode.DeadlineExceeded"/>.
    /// </summary>
    /// <param name="exception">What the call threw.</param>
    /// <returns><c>true</c> for an unreachable or silent agent.</returns>
    /// <remarks>
    /// <para>
    /// Wider than <see cref="IsConnectFailure"/>: it includes a failure after the request was sent. So
    /// it is safe to RETRY on only for a call that changes nothing — the handshake, which is what the
    /// probe pipeline carries — and never for an operation.
    /// </para>
    /// <para>
    /// The panel sets no gRPC deadline of its own; its time limit is the pipeline's timeout, which
    /// arrives as Polly's <c>TimeoutRejectedException</c> and never as a status. A
    /// <see cref="StatusCode.DeadlineExceeded"/> that reaches this method is therefore one the other
    /// side reported, and it means the same thing: nothing answered in time.
    /// </para>
    /// </remarks>
    public static bool IsUnreachable(Exception exception)
    {
        return exception is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded };
    }

    /// <summary>The typed failure the panel answers a transport failure with.</summary>
    /// <param name="exception">The status the transport raised.</param>
    /// <returns>An error carrying a machine-stable code and its kind; never the transport's own text.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="StatusCode.Unavailable"/>, <see cref="StatusCode.DeadlineExceeded"/> and
    /// <see cref="StatusCode.Cancelled"/> are one answer, <c>AgentUnavailable</c> with
    /// <see cref="ErrorType.Unavailable"/> — a 503 that says "try again" rather than "you were wrong" or
    /// "something unexpected broke". Its sentence does not claim that nothing changed: a failure after
    /// the request was sent may follow work the agent completed.
    /// </para>
    /// <para>
    /// A <see cref="StatusCode.Cancelled"/> that arrives here is not the caller giving up.
    /// <see cref="Channels.AgentChannel"/> builds its channel with
    /// <c>ThrowOperationCanceledOnCancellation</c>, so a cancelled token surfaces as an
    /// <see cref="OperationCanceledException"/> and never as this status; what is left is a call the
    /// other side cancelled. A caller holding a token still checks it before mapping — a channel built
    /// any other way, a test's for instance, reports the caller's own cancellation as this status, and
    /// answering that with "the agent is unavailable" would blame the server for the client leaving.
    /// </para>
    /// <para>
    /// <see cref="StatusCode.Unimplemented"/> is the agent saying it does not serve this rpc, which is
    /// the transport-level twin of the payload's <c>ERROR_CODE_NOT_IMPLEMENTED</c>, so it takes the same
    /// code. Every other status — the peer-credential guard's <see cref="StatusCode.PermissionDenied"/>,
    /// an <see cref="StatusCode.Internal"/> from the server framework — is a server fault the caller
    /// could not have influenced, answered as <c>AgentInvalidResponse</c> with
    /// <see cref="ErrorType.Failure"/>.
    /// </para>
    /// <para>
    /// The status detail is never carried outward: it names the socket path and the host's own socket
    /// error, which a customer must not read (rules/security.md item 8).
    /// </para>
    /// </remarks>
    public static Error ToError(RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.StatusCode switch
        {
            StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled =>
                Unavailable(),
            StatusCode.Unimplemented =>
                Error.Of(nameof(ErrorMessages.AgentNotImplemented), ErrorType.Failure),
            _ => Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure),
        };
    }

    /// <summary>
    /// The failure for an agent that could not be reached or did not answer within the panel's own
    /// deadline — the same answer whether the transport or the panel's timeout gave up first.
    /// </summary>
    /// <returns><c>AgentUnavailable</c>, <see cref="ErrorType.Unavailable"/>.</returns>
    public static Error Unavailable()
    {
        return Error.Of(nameof(ErrorMessages.AgentUnavailable), ErrorType.Unavailable);
    }
}
