namespace Maran.Modules.Accounts.Common;

/// <summary>
/// The facts of a refused plan that does not fit its reseller's envelope: which of the plan's figures
/// are over what the reseller was sold. Published as the <c>envelope</c> extension of the problem
/// response beside the code <c>ResellerPlanExceedsEnvelope</c>.
/// </summary>
/// <param name="Fields">
/// The names of the offending figures, spelled as the plan's own JSON members are (<c>diskQuotaMb</c>,
/// <c>features</c>, <c>cpuQuotaPercent</c>), so a form can mark the field. Never a figure of the
/// envelope: the reseller reads its envelope on its own screen, and an error is not the place to
/// repeat it.
/// </param>
public sealed record PlanEnvelopeViolationsDto(IReadOnlyList<string> Fields);
