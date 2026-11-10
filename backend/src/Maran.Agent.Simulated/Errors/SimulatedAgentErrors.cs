using Maran.Agent.Client.Errors;

namespace Maran.Agent.Simulated.Errors;

/// <summary>
/// The failures a simulated agent answers with: the same code and the same kind the real client
/// produces for the same agent refusal, so a handler cannot tell which agent it is talking to.
/// </summary>
/// <remarks>
/// The codes are the keys of <c>Maran.Agent.Client</c>'s <c>ErrorMessages</c> table, which is internal
/// to that project, so they are spelled here as constants rather than through <c>nameof</c>. The kind
/// beside each code is the one <c>AgentErrorTranslator.ToErrorType</c> assigns to the wire code it
/// stands for. A test reads every constant here back from the agent client's table, so a code that
/// is renamed or removed there fails this project's tests instead of reaching a customer as its own
/// spelling.
/// </remarks>
public static class SimulatedAgentErrors
{
    /// <summary>The thing being created is already there.</summary>
    public const string AlreadyExistsCode = "AgentAlreadyExists";

    /// <summary>The thing being read, changed or removed is not there.</summary>
    public const string NotFoundCode = AgentErrorCodes.NotFound;

    /// <summary>The request carried a value the agent refuses outright.</summary>
    public const string InvalidInputCode = "AgentInvalidInput";

    /// <summary>The request was well formed but the state it meets refuses it as a whole.</summary>
    public const string ValidationFailedCode = "AgentValidationFailed";

    /// <summary>A stated or built-in ceiling on how many of a thing exist is already met.</summary>
    public const string LimitReachedCode = "AgentLimitReached";

    /// <summary>The agent refuses to act for an account the panel has suspended.</summary>
    public const string AccountSuspendedCode = "AgentAccountSuspended";

    /// <summary>The operation is not one this agent performs for the destination it was asked about.</summary>
    public const string NotImplementedCode = "AgentNotImplemented";

    /// <summary>The server cannot take on more data until space is freed.</summary>
    public const string BelowFreeSpaceFloorCode = "AgentBelowFreeSpaceFloor";

    /// <summary>
    /// The firewall was asked to render a ruleset without the host ports that keep the operator connected.
    /// </summary>
    public const string FirewallPortsMisconfiguredCode = "AgentFirewallPortsMisconfigured";

    /// <summary>The thing being created is already there (kind: conflict).</summary>
    /// <returns>The failure.</returns>
    public static Error AlreadyExists()
    {
        return Error.Of(AlreadyExistsCode, ErrorType.Conflict);
    }

    /// <summary>The thing being read, changed or removed is not there (kind: not found).</summary>
    /// <returns>The failure.</returns>
    public static Error NotFound()
    {
        return Error.Of(NotFoundCode, ErrorType.NotFound);
    }

    /// <summary>The request carried a value the agent refuses outright (kind: validation).</summary>
    /// <returns>The failure.</returns>
    public static Error InvalidInput()
    {
        return Error.Of(InvalidInputCode, ErrorType.Validation);
    }

    /// <summary>The state the request meets refuses it as a whole (kind: validation).</summary>
    /// <returns>The failure.</returns>
    public static Error ValidationFailed()
    {
        return Error.Of(ValidationFailedCode, ErrorType.Validation);
    }

    /// <summary>A ceiling on how many of a thing exist is already met (kind: conflict).</summary>
    /// <returns>The failure.</returns>
    public static Error LimitReached()
    {
        return Error.Of(LimitReachedCode, ErrorType.Conflict);
    }

    /// <summary>The account is suspended and the agent will not act for it (kind: conflict).</summary>
    /// <returns>The failure.</returns>
    public static Error AccountSuspended()
    {
        return Error.Of(AccountSuspendedCode, ErrorType.Conflict);
    }

    /// <summary>The agent does not perform this operation for this destination (kind: unavailable).</summary>
    /// <returns>The failure.</returns>
    public static Error NotImplemented()
    {
        return Error.Of(NotImplementedCode, ErrorType.Unavailable);
    }

    /// <summary>The server is too close to full to take more data (kind: unavailable).</summary>
    /// <returns>The failure.</returns>
    public static Error BelowFreeSpaceFloor()
    {
        return Error.Of(BelowFreeSpaceFloorCode, ErrorType.Unavailable);
    }

    /// <summary>The host ports the firewall needs were not supplied or are unusable (kind: failure).</summary>
    /// <returns>The failure.</returns>
    public static Error FirewallPortsMisconfigured()
    {
        return Error.Of(FirewallPortsMisconfiguredCode, ErrorType.Failure);
    }
}
