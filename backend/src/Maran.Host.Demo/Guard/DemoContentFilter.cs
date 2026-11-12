using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Guard.Exceptions;
using Maran.Host.Demo.Services;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Refuses a write whose visitor-typed text carries a blocked term or a link off the allow-list, before the
/// action stores it, so abusive text is never kept and never shown to the next visitor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a filter and not a table row.</b> The refusal table classifies by action and a clean write is
/// allowed; what is refused is the value of some member of its body. An action filter sees the bound
/// arguments, so it judges every action the table marks as screened (<see cref="DemoEndpointRule.ScreensText"/>)
/// without the table naming a field. It is the same shape as <see cref="DemoInitialPasswordFilter"/>.
/// </para>
/// <para>
/// <b>Reads are never judged</b>: the table holds only mutating actions, so a search box is not screened. A
/// call that did not pass the table's rule never reaches here (the middleware answered it first). The answer
/// is a 400 with the code <c>DemoContentRefused</c> in the caller's language and the member's name, never the
/// text; the refusal is journaled by action and member only. Arguments the walk cannot read in full (nested
/// beyond its depth, too many values, a getter that throws) are refused the same way, because text it did not
/// reach would be stored unscreened.
/// </para>
/// </remarks>
public sealed class DemoContentFilter : IAsyncActionFilter
{
    /// <summary>
    /// The most characters one request may put through the screen. Every text runs through several patterns,
    /// each with its own time limit, and a request may carry up to <see cref="DemoFreeText.MaximumNodes"/> values,
    /// so without a total the cost of one request is bounded by nothing a visitor cannot raise. A request past
    /// it is refused like text the screen could not read; half a million characters is far above anything a
    /// panel form or an edited file holds.
    /// </summary>
    public const int MaximumScreenedCharacters = 500_000;

    /// <summary>The table of controller actions.</summary>
    private readonly DemoEndpointPolicy _policy;

    /// <summary>The text screen.</summary>
    private readonly DemoTextScreen _screen;

    /// <summary>What the journal may still be asked to write.</summary>
    private readonly DemoAuditAllowance _allowance;

    /// <summary>The guard's audit journal, scoped to the request.</summary>
    private readonly DemoAuditJournal _journal;

    /// <summary>Creates the filter.</summary>
    /// <param name="policy">The table of controller actions.</param>
    /// <param name="screen">The text screen.</param>
    /// <param name="allowance">What the journal may still be asked to write.</param>
    /// <param name="journal">The guard's audit journal, scoped to the request.</param>
    public DemoContentFilter(
        DemoEndpointPolicy policy,
        DemoTextScreen screen,
        DemoAuditAllowance allowance,
        DemoAuditJournal journal)
    {
        _policy = policy;
        _screen = screen;
        _allowance = allowance;
        _journal = journal;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var refusedField = FindRefusedField(context);
        if (refusedField is null)
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        if (_allowance.TryTakeRefusal())
        {
            await _journal.RecordContentRefusalAsync(
                DemoWriteGuard.LabelOf(http.Request.Method, http.GetEndpoint()),
                refusedField,
                ClientAddress.Of(http.Connection.RemoteIpAddress),
                http.Request.Headers.UserAgent.ToString(),
                http.RequestAborted);
        }

        await DemoProblemWriter.WriteContentRefusedAsync(http, refusedField);
        context.Result = new EmptyResult();
    }

    /// <summary>Finds the first member of the bound arguments whose text the screen refuses.</summary>
    /// <param name="context">The action about to run.</param>
    /// <returns>The member's name, or <c>null</c> when the action is not screened or every text is clean.</returns>
    private string? FindRefusedField(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            return null;
        }

        var rule = _policy.RuleFor(DemoEndpointPolicy.ActionIdOf(descriptor.MethodInfo));
        if (rule is null || !rule.ScreensText)
        {
            return null;
        }

        try
        {
            var remaining = MaximumScreenedCharacters;
            foreach (var (field, text) in DemoFreeText.Of(context.ActionArguments))
            {
                remaining -= text.Length;
                if (remaining < 0 || _screen.Judge(text) != DemoTextVerdict.Clean)
                {
                    return MemberOf(field);
                }
            }
        }
        catch (DemoFreeTextUnreadableException unreadable)
        {
            // Text the walk could not reach would be stored unscreened, so the write is refused like a blocked one.
            return MemberOf(unreadable.Field);
        }

        return null;
    }

    /// <summary>The name a visitor can act on: the member's, without the parameter it was bound to.</summary>
    /// <param name="field">The dotted name, beginning with the parameter's name.</param>
    /// <returns>The member's dotted name, or the parameter's own when the text is the argument itself.</returns>
    private static string MemberOf(string field)
    {
        // The root is the parameter's name, which means nothing to a visitor; the member's name does.
        var dot = field.IndexOf('.', StringComparison.Ordinal);
        return dot >= 0 ? field[(dot + 1)..] : field;
    }
}
