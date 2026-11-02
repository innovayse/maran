using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Resources;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Maran.Sdk.Filters;

/// <summary>
/// Refuses a caller acting for an account whose plan does not include the feature a controller
/// belongs to, independently of what the menu offers (<see cref="RequiresAccountFeatureAttribute"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Who it applies to.</b> A caller whose token names an account (a <see cref="CustomerCaller"/>).
/// An administrator is unaffected, with or without an account id: the plan limits what a customer is
/// SOLD, not what the person running the server may do. A caller with no account is passed on to the
/// handler, which answers "no account" in its own words.
/// </para>
/// <para>
/// <b>Two questions, two refusals.</b> The plan is asked first (<c>PlanFeatureNotIncluded</c>), then
/// the person (<c>AccountFeatureNotGranted</c>, <see cref="IAccountMemberGrants"/>): an owner passes the
/// second always, a team member only for the features the owner gave them. Both are 403 for the reason
/// below, and neither depends on what the menu shows.
/// </para>
/// <para>
/// <b>The refusal is <see cref="ErrorType.Forbidden"/> (403) with <c>PlanFeatureNotIncluded</c>, not
/// 404.</b> rules/csharp.md reserves <c>NotFound</c> for another tenant's row, because a 403 there
/// confirms the row exists. Nothing of the kind is at stake here: the caller is asking about their
/// own plan, the answer names nothing about any other account, and the endpoint itself is public
/// knowledge. A 404 would be a lie about an endpoint that exists and would send the customer looking
/// for a missing page; 403 says the true thing — the plan does not include this. It is also not a
/// <c>Conflict</c>: nothing about the request clashes with a state, the caller simply may not.
/// </para>
/// <para>
/// <b>Order.</b> It runs as an action filter, after authentication and the endpoint's authorization
/// policy, so an unauthenticated call is still a 401 and never reveals which features exist. The
/// check reads the plan on every call, so an administrator's edit takes effect on the customer's next
/// request without a new sign-in.
/// </para>
/// </remarks>
public sealed class RequiresAccountFeatureFilter : IAsyncActionFilter
{
    /// <summary>The feature the caller's plan must include.</summary>
    private readonly AccountFeature _feature;

    /// <summary>The authenticated principal of the current request.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Answers whether the caller's plan includes the feature.</summary>
    private readonly IAccountFeatureDirectory _features;

    /// <summary>Answers whether the caller, as a person, has been given the feature on that account.</summary>
    private readonly IAccountMemberGrants _grants;

    /// <summary>Creates the filter; the feature comes from the attribute, the rest from the container.</summary>
    /// <param name="feature">The feature the caller's plan must include.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="features">Answers whether the caller's plan includes the feature.</param>
    /// <param name="grants">Answers whether the caller, as a person, has been given the feature.</param>
    public RequiresAccountFeatureFilter(
        AccountFeature feature,
        ICurrentUser currentUser,
        IAccountFeatureDirectory features,
        IAccountMemberGrants grants)
    {
        _feature = feature;
        _currentUser = currentUser;
        _features = features;
        _grants = grants;
    }

    /// <summary>Lets the action run, or refuses the call when the caller's plan lacks the feature.</summary>
    /// <param name="context">The action about to run; its result is set to the refusal.</param>
    /// <param name="next">Runs the action.</param>
    /// <returns>A task that completes when the action has run or the refusal has been set.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_currentUser.Caller is not CustomerCaller customer)
        {
            await next();
            return;
        }

        var accountId = customer.AccountId;

        if (!await _features.IncludesAsync(accountId, _feature, context.HttpContext.RequestAborted))
        {
            var refusal = Error.Of(nameof(ValidationMessages.PlanFeatureNotIncluded), ErrorType.Forbidden);
            context.Result = Result.Fail(refusal).ToActionResult(context.HttpContext);
            return;
        }

        // Second, independent question: the plan sells the feature, but has the account's owner given
        // it to THIS person? An owner always has it; a team member only when it was granted, whatever
        // the menu offers. See IAccountMemberGrants for why a person with no standing is refused.
        if (!await _grants.PermitsAsync(
                customer.UserId, accountId, _feature, context.HttpContext.RequestAborted))
        {
            var refusal = Error.Of(nameof(ValidationMessages.AccountFeatureNotGranted), ErrorType.Forbidden);
            context.Result = Result.Fail(refusal).ToActionResult(context.HttpContext);
            return;
        }

        await next();
    }
}
