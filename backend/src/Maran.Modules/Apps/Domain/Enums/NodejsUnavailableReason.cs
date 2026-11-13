namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>Why the installable list carries no Node.js app that is not already installed (issue #532).</summary>
public enum NodejsUnavailableReason
{
    /// <summary>The host's package sources carry no Node.js module streams (a release without modularity). An answer, not a fault.</summary>
    NoModuleStreams = 1,

    /// <summary>The module listing could not be run or read, so what is offered is unknown and a retry may succeed.</summary>
    ListingFailed = 2,
}
