namespace Maran.Modules.Monitoring.Queries.GetRebootRequirement;

/// <summary>
/// Reads whether the host must be restarted for the updates already installed to take full effect,
/// live from the agent (issue #163).
/// </summary>
/// <remarks>
/// Takes no parameters, and so has no validator: there is one host and the rpc behind it accepts
/// nothing from a caller, so there is no member whose bounds could need holding.
/// </remarks>
public sealed record GetRebootRequirementQuery;
