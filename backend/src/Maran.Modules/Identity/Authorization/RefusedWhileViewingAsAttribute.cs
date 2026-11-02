namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Marks an endpoint a view-as token may not reach: anything that would let the operator change the
/// viewed login's credentials, second factor or sessions, or start viewing as somebody else
/// (issue #317).
/// </summary>
/// <remarks>
/// <para>
/// <b>It refuses; it never grants.</b> The action's own <c>[Authorize]</c> still decides who may call
/// it at all. This only adds a refusal for a view-as token, enforced by
/// <see cref="ViewAsBoundaryHandler"/>, which every panel policy carries.
/// </para>
/// <para>
/// <b>A deny-list, and why that is acceptable here.</b> Viewing as a customer is meant to let the
/// operator do what the customer can do — that is how support follows a customer's report — so the
/// set to refuse is the small set that would turn a look into a takeover: the credential, the second
/// factor, the session list, signing out everywhere, and a second view-as. A new endpoint touching
/// any of those must carry this attribute; the threat note's reviewer checklist names that grep.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RefusedWhileViewingAsAttribute : Attribute
{
}
