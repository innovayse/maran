using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Errors;

/// <summary>
/// The panel declined to dial a server: it is unknown, not yet approved or switched off. Raised from
/// the channel lookup, which has no result to carry the refusal, and turned back into a failed result
/// by the client method that made the call.
/// </summary>
public sealed class AgentEndpointRefusedException : Exception
{
    /// <summary>The typed failure to answer with: a machine-stable code and its kind, no text.</summary>
    public Error Error { get; }

    /// <summary>Creates the exception for a refusal.</summary>
    /// <param name="error">Why the server was not dialled.</param>
    public AgentEndpointRefusedException(Error error)
        : base($"The agent endpoint was refused: {error.Code}.")
    {
        Error = error;
    }
}
