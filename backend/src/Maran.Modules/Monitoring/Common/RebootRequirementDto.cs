namespace Maran.Modules.Monitoring.Common;

/// <summary>Whether the host must be restarted for the updates already installed to take full effect.</summary>
/// <param name="Required">
/// <c>true</c> when the host's own marker says a restart is needed, <c>false</c> when it says none is,
/// or <c>null</c> when that was not measured (an agent that predates the reading). Null and never
/// <c>false</c>, so the interface shows no notice rather than a false "nothing to restart". A probe
/// that could not run is not a value here at all: it is the agent's own typed failure.
/// </param>
public sealed record RebootRequirementDto(bool? Required);
