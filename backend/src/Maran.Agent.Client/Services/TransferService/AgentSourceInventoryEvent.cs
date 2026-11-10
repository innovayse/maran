namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One event of an inventory read of a registered source: progress, a finding, or the way the read ended.</summary>
/// <remarks>
/// <para>
/// One of exactly five cases, each its own type, because they share nothing: a step reached
/// (<see cref="AgentSourceInventoryProgress"/>), one account (<see cref="AgentSourceInventoryAccountFound"/>),
/// one warning (<see cref="AgentSourceInventoryWarningRaised"/>), and the two endings
/// (<see cref="AgentSourceInventoryDone"/>, <see cref="AgentSourceInventoryFailed"/>). A flat record with a
/// kind and every member optional would let a caller read an account off a warning.
/// </para>
/// <para>
/// <b>Every sequence ends in exactly one ending.</b> A read that stops without the agent stating an
/// outcome (the stream ended short, or the transport broke) ends in <see cref="AgentSourceInventoryFailed"/>,
/// never in a quiet end of the sequence: an inventory missing its tail must not be recorded as complete.
/// </para>
/// </remarks>
public abstract record AgentSourceInventoryEvent
{
    /// <summary>Closes the hierarchy to the five cases this client declares.</summary>
    private protected AgentSourceInventoryEvent()
    {
    }
}
