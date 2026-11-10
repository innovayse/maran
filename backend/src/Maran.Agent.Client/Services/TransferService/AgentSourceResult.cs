using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The factories of <see cref="AgentSourceResult{T}"/>, on a non-generic type so the value type is inferred or named once.</summary>
public static class AgentSourceResult
{
    /// <summary>A successful result.</summary>
    /// <typeparam name="T">The value the call produced.</typeparam>
    /// <param name="value">What the call produced.</param>
    /// <returns>The result.</returns>
    public static AgentSourceResult<T> Ok<T>(T value)
    {
        return new AgentSourceResult<T>(true, value, null, SourceFailureReason.Unspecified);
    }

    /// <summary>A failed result.</summary>
    /// <typeparam name="T">The value a successful call would have produced.</typeparam>
    /// <param name="error">The typed failure.</param>
    /// <param name="reason">Why the source refused, when the agent said; otherwise <see cref="SourceFailureReason.Unspecified"/>.</param>
    /// <returns>The result.</returns>
    public static AgentSourceResult<T> Fail<T>(Error error, SourceFailureReason reason = SourceFailureReason.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new AgentSourceResult<T>(false, default, error, reason);
    }
}
