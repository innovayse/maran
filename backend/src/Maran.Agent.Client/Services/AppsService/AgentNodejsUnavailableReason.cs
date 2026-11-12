namespace Maran.Agent.Client.Services.AppsService;

/// <summary>Why the Node.js apps that are not installed are missing from the installable list (issue #532).</summary>
/// <remarks>
/// Not named after the wire enum: the generated types may not leave this project. "No reason" is not a member
/// here; it is an absent value on <see cref="AgentInstallableApps.NodejsUnavailable"/>, so the wire's zero never
/// reaches a reader.
/// </remarks>
public enum AgentNodejsUnavailableReason
{
    /// <summary>The package sources carry no Node.js module streams (a release without modularity). An answer, not a fault.</summary>
    NoModuleStreams = 1,

    /// <summary>The module listing could not be run or read, so what is offered is unknown and a retry may succeed.</summary>
    ListingFailed = 2,
}
