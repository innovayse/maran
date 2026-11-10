using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// The outcome of one unary call to a registered source: a value, or a typed failure together with why the
/// source refused when the agent could say.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Result{T}"/> carries a code and nothing else, and the reason a source call failed is not a
/// code: host key changed, key refused and host unreachable are three different things an administrator
/// does next, and all three arrive as the same agent failure. The reason is a second answer about the same
/// failure, so it rides beside the <see cref="Error"/> instead of being folded into a new error code that
/// every table of codes would have to learn.
/// </para>
/// <para>
/// A failure of this host (the agent could not be reached, the request was invalid) carries
/// <see cref="SourceFailureReason.Unspecified"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">The value a successful call produces.</typeparam>
public sealed class AgentSourceResult<T>
{
    /// <summary>The value of a successful call; unset on a failure.</summary>
    private readonly T? _value;

    /// <summary>Whether the call succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The typed failure, or <c>null</c> on success.</summary>
    public Error? Error { get; }

    /// <summary>Why the source refused, or <see cref="SourceFailureReason.Unspecified"/> when it did not or on success.</summary>
    public SourceFailureReason Reason { get; }

    /// <summary>The value of a successful call.</summary>
    /// <exception cref="InvalidOperationException">The call failed.</exception>
    public T Value
    {
        get
        {
            return IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {Error!.Code}");
        }
    }

    /// <summary>Creates a result; reached only through <see cref="AgentSourceResult.Ok{T}"/> and <see cref="AgentSourceResult.Fail{T}"/>.</summary>
    /// <param name="success">Whether the call succeeded.</param>
    /// <param name="value">The value, for a success.</param>
    /// <param name="error">The failure, for a failure.</param>
    /// <param name="reason">Why the source refused.</param>
    internal AgentSourceResult(bool success, T? value, Error? error, SourceFailureReason reason)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
        Reason = reason;
    }
}
