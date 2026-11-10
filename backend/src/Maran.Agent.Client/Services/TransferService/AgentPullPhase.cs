namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Which pass of a migration a pull of an account's files is.</summary>
public enum AgentPullPhase
{
    /// <summary>The first copy of a live source: nothing is removed from the destination.</summary>
    Initial = 1,

    /// <summary>The delta at cut-over: the destination is made to equal the source, deleting what the source no longer has.</summary>
    Final = 2,
}
