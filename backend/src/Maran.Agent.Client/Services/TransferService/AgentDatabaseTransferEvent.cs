namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One message of a database transfer; a stream ends with exactly one done or failed event.</summary>
public abstract record AgentDatabaseTransferEvent
{
    /// <summary>Closed to this assembly: the cases are the ones the agent's stream has.</summary>
    private protected AgentDatabaseTransferEvent()
    {
    }
}
