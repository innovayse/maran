namespace Maran.Agent.Client.Services.TransferService;

/// <summary>What kind of host a registered source turned out to be.</summary>
/// <remarks>
/// A host whose kind the probes could not determine has no value of this type at all
/// (<see cref="AgentTestedSource.Kind"/> is <c>null</c>); the wire's "not determined" is stated once, in
/// <c>TransferMapper</c>, and never reaches the panel as a value that could be read as an answer.
/// </remarks>
public enum SourceKind
{
    /// <summary>A Linux host with no panel recognised: the generic adapter reads it.</summary>
    GenericLinux = 0,

    /// <summary>A host running a Maran panel.</summary>
    Maran = 1,
}
