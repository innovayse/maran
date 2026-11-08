namespace Maran.Agent.Client.Services.DnsService;

/// <summary>Whether the agent could see a safety property of the running name server, and what it saw.</summary>
public enum DnsPolicyState
{
    /// <summary>The agent did not look, or could not tell. Never read as "in force".</summary>
    NotObserved,

    /// <summary>The running daemon was seen to hold the property.</summary>
    InForce,

    /// <summary>The running daemon was seen NOT to hold the property.</summary>
    NotInForce,
}
