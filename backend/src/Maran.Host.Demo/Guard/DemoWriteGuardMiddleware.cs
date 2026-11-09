using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Services;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// The demo's gate for every mutating request: refuse what the table refuses, then spend the mutation
/// budgets for what it lets through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it sits.</b> After authentication and authorization, so an unauthenticated call is still a
/// 401 and the journal knows who was refused; after the pseudonymising middleware, so the visitor key
/// and the journaled address are pseudonyms. It is a middleware and not an MVC filter because it must
/// also judge minimal endpoints and a marketplace module's endpoints, which a filter never sees.
/// </para>
/// <para>
/// <b>Reads are never judged</b>, and a refusal spends no budget.
/// </para>
/// </remarks>
public sealed class DemoWriteGuardMiddleware
{
    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>The classification.</summary>
    private readonly DemoWriteGuard _guard;

    /// <summary>The mutation budgets.</summary>
    private readonly DemoMutationBudget _budget;

    /// <summary>The sign-in, refresh and sign-out allowances.</summary>
    private readonly DemoSignInBudget _signIns;

    /// <summary>What the journal may still be asked to write.</summary>
    private readonly DemoAuditAllowance _allowance;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="guard">The classification.</param>
    /// <param name="budget">The mutation budgets.</param>
    /// <param name="signIns">The sign-in, refresh and sign-out allowances.</param>
    /// <param name="allowance">What the journal may still be asked to write.</param>
    /// <param name="clock">The time source.</param>
    public DemoWriteGuardMiddleware(
        RequestDelegate next,
        DemoWriteGuard guard,
        DemoMutationBudget budget,
        DemoSignInBudget signIns,
        DemoAuditAllowance allowance,
        IClock clock)
    {
        _next = next;
        _guard = guard;
        _budget = budget;
        _signIns = signIns;
        _allowance = allowance;
        _clock = clock;
    }

    /// <summary>Judges the request, then hands it on or answers it.</summary>
    /// <param name="context">The current request.</param>
    /// <param name="journal">The guard's audit journal, scoped to the request.</param>
    /// <returns>Resolves once the request has been handled or refused.</returns>
    public async Task InvokeAsync(HttpContext context, DemoAuditJournal journal)
    {
        var method = context.Request.Method;
        var endpoint = context.GetEndpoint();
        var decision = _guard.Decide(method, endpoint, context.Request.RouteValues);
        var address = ClientAddress.Of(context.Connection.RemoteIpAddress);
        var userAgent = context.Request.Headers.UserAgent.ToString();

        if (!decision.Permitted)
        {
            if (_allowance.TryTakeRefusal())
            {
                await journal.RecordRefusalAsync(
                    DemoWriteGuard.LabelOf(method, endpoint),
                    DemoRefusalAreaIds.IdOf(decision.Area!.Value),
                    address,
                    userAgent,
                    context.RequestAborted);
            }

            await DemoProblemWriter.WriteRefusedAsync(context, decision.Area!.Value);
            return;
        }

        if (decision.CountsTowardBudget)
        {
            switch (_budget.TrySpend(address, _clock.UtcNow, out var retryAfter))
            {
                case DemoBudgetDecision.VisitorRateExceeded:
                    await DemoProblemWriter.WriteRateLimitedAsync(context, retryAfter);
                    return;
                case DemoBudgetDecision.GlobalExhausted:
                    if (_allowance.TryTakeExhaustion())
                    {
                        await journal.RecordBudgetExhaustedAsync(address, userAgent, context.RequestAborted);
                    }

                    await DemoProblemWriter.WriteBudgetExhaustedAsync(context);
                    return;
            }
        }

        if (decision.CountsTowardSignInBudget
            && _signIns.TryTake(address, _clock.UtcNow, out var signInRetryAfter) != DemoBudgetDecision.Granted)
        {
            await DemoProblemWriter.WriteRateLimitedAsync(context, signInRetryAfter);
            return;
        }

        await _next(context);
    }
}
