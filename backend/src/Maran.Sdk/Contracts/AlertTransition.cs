namespace Maran.Sdk.Contracts;

/// <summary>What happened to the condition an <see cref="OperatorAlert"/> reports.</summary>
/// <remarks>
/// Not Monitoring's own transition enum, which also has a <c>None</c> for "nothing changed": an alert
/// that reaches the dispatcher always reports something, so the absence is not representable here.
/// </remarks>
public enum AlertTransition
{
    /// <summary>A condition that lasts began: the disk filled, a service stopped.</summary>
    Raised = 1,

    /// <summary>A condition that lasted ended.</summary>
    Resolved = 2,

    /// <summary>A one-off event with no duration: a run failed, a certificate came due.</summary>
    Occurred = 3,
}
