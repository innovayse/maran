namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Marks the endpoints an administrator who is being steered into two-factor enrolment may still
/// reach. Everything not carrying it is refused while the steering is in force.
/// </summary>
/// <remarks>
/// <para>
/// <b>An attribute rather than a list of paths.</b> A list in the authorization handler would be a
/// second place the enrolment flow is described, and the failure mode of the two drifting apart is
/// silent in the dangerous direction: a route renamed in the controller and not in the list stops
/// being reachable, an endpoint added to the list and never removed stays reachable to a steered
/// caller for ever. The marker travels with the action it exempts, so there is nothing to keep in
/// step.
/// </para>
/// <para>
/// <b>It exempts nothing on its own.</b> The action still declares its own <c>[Authorize]</c>; this
/// only says the steering does not additionally refuse it. An attribute that granted access would be
/// one forgetful copy-paste away from opening an endpoint.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowDuringTwoFactorEnrolmentAttribute : Attribute
{
}
