namespace Maran.Sdk.Contracts;

/// <summary>
/// Declares what a view-as token may do at an endpoint (issue #334). Placed on an action, or on a
/// controller to cover its actions; an action's own declaration wins over its controller's.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list.</b> While an operator views the panel as a customer login, an endpoint is
/// reachable only when it carries <see cref="ViewAsAccess.Read"/> (on a GET or HEAD) or
/// <see cref="ViewAsAccess.SafeMutation"/>. An endpoint with no declaration is refused, which is why
/// the declaration lives in the Sdk: a marketplace module's controller is code this repository never
/// reviews, and the safe default has to apply to it too.
/// </para>
/// <para>
/// <b>It grants nothing by itself.</b> The action's own <c>[Authorize]</c> still decides who may call
/// it at all; this only says what a view-as token is additionally allowed. It is enforced centrally
/// by the Identity module's view-as authorization requirement, which every panel policy carries, and
/// held for every controller action by the architecture tests.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ViewAsAccessAttribute : Attribute
{
    /// <summary>What a view-as token may do at the endpoint carrying this declaration.</summary>
    public ViewAsAccess Access { get; }

    /// <summary>Declares the access.</summary>
    /// <param name="access">What a view-as token may do here.</param>
    public ViewAsAccessAttribute(ViewAsAccess access)
    {
        Access = access;
    }
}
