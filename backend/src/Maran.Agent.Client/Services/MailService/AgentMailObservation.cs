namespace Maran.Agent.Client.Services.MailService;

/// <summary>A fact about the host that the agent either established or could not.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>MailObservation</c>, so callers outside this project never hold
/// a generated protobuf type. Three values and not a boolean, because "no" and "I could not tell" are
/// different statements: a screen that folded them would show a healthy stack over a daemon nobody
/// looked at. <see cref="NotObserved"/> is also the wire's UNSPECIFIED read once at the client
/// boundary (an agent that predates the field), so the panel's model never carries the wire's zero as
/// a verdict. A caller must never treat <see cref="NotObserved"/> as <see cref="Yes"/> or as
/// <see cref="No"/>.
/// </remarks>
public enum AgentMailObservation
{
    /// <summary>The agent tried and could not tell, or the agent does not report this fact.</summary>
    NotObserved = 0,

    /// <summary>Observed to be true.</summary>
    Yes = 1,

    /// <summary>Observed to be false.</summary>
    No = 2,
}
