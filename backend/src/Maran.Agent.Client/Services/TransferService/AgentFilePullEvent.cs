namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One message of a pull of files; a stream ends with exactly one done or failed event.</summary>
public abstract record AgentFilePullEvent
{
    /// <summary>Closed to this assembly: the cases are the ones the agent's stream has.</summary>
    private protected AgentFilePullEvent()
    {
    }
}
