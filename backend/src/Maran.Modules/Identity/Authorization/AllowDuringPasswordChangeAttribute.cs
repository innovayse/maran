namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Marks the endpoints a caller who still holds a first password somebody else chose may reach:
/// the password change itself. Everything not carrying it is refused while the gate is in force.
/// </summary>
/// <remarks>
/// <para>
/// <b>An attribute rather than a list of paths</b>, for the reason
/// <see cref="AllowDuringTwoFactorEnrolmentAttribute"/> gives: a list in the authorization handler is a
/// second place the flow is described, and a marker travelling with the action has nothing to keep in
/// step.
/// </para>
/// <para>
/// <b>It exempts nothing on its own.</b> The action still declares its own <c>[Authorize]</c>; this
/// only says the gate does not additionally refuse it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowDuringPasswordChangeAttribute : Attribute
{
}
